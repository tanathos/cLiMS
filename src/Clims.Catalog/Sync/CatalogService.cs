using Clims.Lms;

namespace Clims.Catalog;

/// <summary>
/// Owns the local index of one server: loads it, keeps its sync status honest, syncs when (and only when) asked, and
/// refreshes single albums on demand. Independent of any UI; events may fire on any thread.
/// </summary>
public interface ICatalogService : IDisposable
{
    /// <summary>The catalogue to browse, or null when there is none yet (not synced and no sync data).</summary>
    Catalog? Current { get; }

    /// <summary>Current sync status.</summary>
    SyncStatus Status { get; }

    /// <summary>Raised when <see cref="Status"/> changes.</summary>
    event Action<SyncStatus>? StatusChanged;

    /// <summary>Raised when <see cref="Current"/> is replaced (loaded from disk, partial first build, finished sync).</summary>
    event Action<Catalog?>? CatalogChanged;

    /// <summary>Raised when the user should be asked whether to sync. Answer with <see cref="SyncAsync"/> or <see cref="DeferSync"/>.</summary>
    event Action<SyncPrompt>? PromptRequested;

    /// <summary>Raised after an on-view refresh changed the tracks of an album.</summary>
    event Action<int>? AlbumRefreshed;

    /// <summary>Loads the saved index and compares it with the server. Never starts a sync.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Compares the index with the server again (after a finished scan, or on reconnect).</summary>
    Task CheckServerAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs a full sync. Call only after the user agreed. Completes when the sync ends (or fails or is cancelled).</summary>
    Task SyncAsync(CancellationToken cancellationToken = default);

    /// <summary>The user chose "Later": do not ask again until the next launch (or until the server changes again).</summary>
    void DeferSync();

    /// <summary>Cancels a running sync; the previous index keeps serving.</summary>
    void CancelSync();

    /// <summary>Re-reads one album's tracks from the server and updates the index. True when something changed.</summary>
    Task<bool> RefreshAlbumAsync(int albumId, CancellationToken cancellationToken = default);

    /// <summary>Follows server events: a finished scan or library change re-checks the server; a dropped connection is noticed.</summary>
    Task RunEventsAsync(ILmsEventStream events, CancellationToken cancellationToken = default);
}

/// <summary>Settings for <see cref="CatalogService"/>.</summary>
public sealed class CatalogServiceOptions
{
    /// <summary>Server UUID from a previous run, so the saved index can be used while the server is unreachable.</summary>
    public string? KnownServerUuid { get; init; }

    /// <summary>Creates the store for a server UUID.</summary>
    public required Func<string, ICatalogStore> StoreFactory { get; init; }

    /// <summary>How often to ask whether the server is still scanning before a sync can continue.</summary>
    public TimeSpan ScanPollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Tracks per request during a sync (the server fails above about 5 000).</summary>
    public int TrackPageSize { get; init; } = 2000;

    /// <summary>Clock, replaceable in tests.</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;
}

/// <inheritdoc />
public sealed class CatalogService : ICatalogService
{
    private static readonly TrackTags SyncTags =
        TrackTags.Artist | TrackTags.ArtistId | TrackTags.AlbumId | TrackTags.TrackNumber | TrackTags.Disc | TrackTags.Duration | TrackTags.Genre;

    private static readonly TrackTags RefreshTags = SyncTags | TrackTags.Url;

    private readonly ILmsClient _client;
    private readonly CatalogServiceOptions _options;
    private readonly object _gate = new();
    private ICatalogStore? _store;
    private Catalog? _current;
    private SyncStatus _status = SyncStatus.Initial;
    private Task? _syncTask;
    private CancellationTokenSource? _syncCts;
    private bool _deferred;
    private bool _promptOutstanding;

    /// <summary>Creates the service. Nothing is loaded or contacted until <see cref="StartAsync"/>.</summary>
    public CatalogService(ILmsClient client, CatalogServiceOptions options)
    {
        _client = client;
        _options = options;
    }

    /// <inheritdoc />
    public event Action<SyncStatus>? StatusChanged;

    /// <inheritdoc />
    public event Action<Catalog?>? CatalogChanged;

    /// <inheritdoc />
    public event Action<SyncPrompt>? PromptRequested;

    /// <inheritdoc />
    public event Action<int>? AlbumRefreshed;

    /// <inheritdoc />
    public Catalog? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <inheritdoc />
    public SyncStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    private bool IsSyncing => _syncTask is { IsCompleted: false };

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_options.KnownServerUuid is { Length: > 0 } uuid)
        {
            OpenStore(uuid);
            if (Current?.Meta is { } meta)
            {
                SetStatus(new SyncStatus(SyncState.Synced, true, null, meta.SyncedAt, "Not yet compared with the server."));
            }
        }

        await CheckServerAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CheckServerAsync(CancellationToken cancellationToken = default)
    {
        if (IsSyncing)
        {
            return;
        }

        ServerStatus server;
        try
        {
            server = await _client.Server.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (LmsAuthenticationException ex)
        {
            SetStatus(new SyncStatus(SyncState.LoginRequired, Current?.Meta is not null, null, Current?.Meta?.SyncedAt, ex.Message));
            return;
        }
        catch (LmsException ex)
        {
            var hasIndex = Current?.Meta is not null;
            SetStatus(new SyncStatus(SyncState.Offline, hasIndex, null, Current?.Meta?.SyncedAt, ex.Message));
            return;
        }

        OpenStore(server.Uuid ?? string.Empty);
        var current = Current;
        var assessment = SyncAssessor.Assess(current?.Meta, server);
        switch (assessment.Kind)
        {
            case AssessmentKind.NoIndex:
                SetStatus(new SyncStatus(SyncState.NotSynced, current is not null, null, null, "The library has not been synced yet."));
                Prompt(new SyncPrompt(SyncPromptReason.FirstBuild, $"{server.TrackCount} tracks, {server.AlbumCount} albums"));
                break;
            case AssessmentKind.UpToDate:
                _deferred = false;
                _promptOutstanding = false;
                SetStatus(new SyncStatus(SyncState.Synced, true, null, current!.Meta!.SyncedAt, null));
                break;
            default:
                SetStatus(new SyncStatus(SyncState.ServerChanged, true, null, current!.Meta!.SyncedAt, assessment.Reason));
                Prompt(new SyncPrompt(SyncPromptReason.ServerChanged, assessment.Reason ?? string.Empty));
                break;
        }
    }

    /// <inheritdoc />
    public Task SyncAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (IsSyncing)
            {
                return _syncTask!;
            }

            _promptOutstanding = false;
            _syncCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _syncTask = RunSyncAsync(_syncCts.Token);
            return _syncTask;
        }
    }

    /// <inheritdoc />
    public void DeferSync()
    {
        _deferred = true;
        _promptOutstanding = false;
    }

    /// <inheritdoc />
    public void CancelSync()
    {
        lock (_gate)
        {
            _syncCts?.Cancel();
        }
    }

    /// <inheritdoc />
    public async Task<bool> RefreshAlbumAsync(int albumId, CancellationToken cancellationToken = default)
    {
        var catalog = Current;
        if (catalog is null || catalog.Album(albumId) is null)
        {
            return false;
        }

        try
        {
            var page = await _client.Library
                .GetTracksAsync(new TrackQuery { AlbumId = albumId, Tags = RefreshTags, Sort = TrackSort.TrackNumber }, 0, 1000, cancellationToken)
                .ConfigureAwait(false);
            var tracks = page.Items.Select(t => ToCatalogTrack(t, albumId)).ToList();
            if (!catalog.ReplaceAlbumTracks(albumId, tracks))
            {
                return false;
            }

            try
            {
                _store?.ReplaceAlbumTracks(albumId, tracks);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
            {
                // The index is only a cache; the in-memory update stands even if the disk write failed.
            }

            AlbumRefreshed?.Invoke(albumId);
            return true;
        }
        catch (LmsException)
        {
            return false; // offline or the server is busy: keep what the index has
        }
    }

    /// <inheritdoc />
    public async Task RunEventsAsync(ILmsEventStream events, CancellationToken cancellationToken = default)
    {
        await foreach (var e in events.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (e is ScanFinishedEvent or LibraryChangedEvent or EventStreamStateEvent)
            {
                await CheckServerAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Cancels a running sync.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _syncCts?.Cancel();
            _syncCts?.Dispose();
            _syncCts = null;
        }
    }

    private async Task RunSyncAsync(CancellationToken ct)
    {
        var hadIndex = Current?.Meta is not null;
        var stages = new Progress();
        SetStatus(new SyncStatus(SyncState.Syncing, Current is not null, stages.Text, Current?.Meta?.SyncedAt, null));
        try
        {
            var server = await _client.Server.GetStatusAsync(ct).ConfigureAwait(false);
            OpenStore(server.Uuid ?? string.Empty);
            await WaitForIdleServerAsync(ct).ConfigureAwait(false);

            var genres = (await FetchAllAsync((s, c, t) => _client.Library.GetGenresAsync(s, c, t), ct).ConfigureAwait(false))
                .Select(g => new CatalogGenre(g.Id, g.Name)).ToList();
            Report(stages, "genres", 1, 1);

            var artists = (await FetchAllAsync((s, c, t) => _client.Library.GetArtistsAsync(null, s, c, t), ct).ConfigureAwait(false))
                .Select(a => new CatalogArtist(a.Id, a.Name)).ToList();
            Report(stages, "artists", 1, 1);

            var albums = (await FetchAllAsync((s, c, t) => _client.Library.GetAlbumsAsync(new AlbumQuery(), s, c, t), ct).ConfigureAwait(false))
                .Select(ToCatalogAlbum).ToList();
            Report(stages, "albums", 1, 1);

            // First build: the library is browsable as soon as artists and albums are in, while tracks stream in.
            // Track lists of visible albums are fetched live in the meantime (the on-view refresh).
            if (!hadIndex)
            {
                Publish(new Catalog(genres, artists, albums, Array.Empty<CatalogTrack>(), null, tracksLoaded: false));
            }

            var tracks = await FetchTracksAsync(stages, ct).ConfigureAwait(false);

            var meta = new CatalogMeta(
                server.Uuid ?? string.Empty,
                server.LastScan?.ToUnixTimeSeconds(),
                server.TrackCount,
                server.AlbumCount,
                server.ArtistCount,
                server.GenreCount,
                _options.Now());
            var full = new Catalog(genres, artists, albums, tracks, meta);
            ct.ThrowIfCancellationRequested();
            await Task.Run(() => _store?.Save(full), CancellationToken.None).ConfigureAwait(false);

            _deferred = false;
            Publish(full);
            SetStatus(new SyncStatus(SyncState.Synced, true, null, meta.SyncedAt, null));
        }
        catch (OperationCanceledException)
        {
            Revert(hadIndex, "Sync cancelled.", offline: false);
        }
        catch (LmsAuthenticationException ex)
        {
            Revert(hadIndex, ex.Message, offline: false, loginRequired: true);
        }
        catch (LmsConnectionException ex)
        {
            Revert(hadIndex, ex.Message, offline: true);
        }
        catch (LmsException ex)
        {
            Revert(hadIndex, "Sync failed: " + ex.Message, offline: false);
        }
    }

    private void Revert(bool hadIndex, string detail, bool offline, bool loginRequired = false)
    {
        if (!hadIndex)
        {
            Publish(null); // drop the partial first-build catalogue
        }

        var current = Current;
        var state = loginRequired ? SyncState.LoginRequired : offline ? SyncState.Offline : hadIndex ? SyncState.ServerChanged : SyncState.NotSynced;
        SetStatus(new SyncStatus(state, current?.Meta is not null, null, current?.Meta?.SyncedAt, detail));
    }

    private async Task<List<CatalogTrack>> FetchTracksAsync(Progress stages, CancellationToken ct)
    {
        // An explicit sort is required: with the genre tag, LMS 9.1.1 drops the connection for sort:title and for no
        // sort at all, but answers sort:albumtrack (about 7 s per 2 000 tracks on a Raspberry Pi).
        var query = new TrackQuery { Tags = SyncTags, Sort = TrackSort.AlbumTrack };
        var all = new List<CatalogTrack>();
        var start = 0;
        var total = int.MaxValue;
        while (start < total)
        {
            await WaitForIdleServerAsync(ct).ConfigureAwait(false);
            var page = await _client.Library.GetTracksAsync(query, start, _options.TrackPageSize, ct).ConfigureAwait(false);
            total = page.Total;
            foreach (var t in page.Items)
            {
                if (t.AlbumId is { } albumId)
                {
                    all.Add(ToCatalogTrack(t, albumId));
                }
            }

            start += page.Items.Count;
            Report(stages, "tracks", Math.Min(start, total), Math.Max(1, total));
            if (page.Items.Count == 0)
            {
                break;
            }
        }

        return all;
    }

    /// <summary>Waits while the server scans: it answers slowly then, and a scan ends with player reconnects.</summary>
    private async Task WaitForIdleServerAsync(CancellationToken ct)
    {
        while (true)
        {
            bool scanning;
            try
            {
                scanning = (await _client.Server.GetScanStateAsync(ct).ConfigureAwait(false)).IsScanning;
            }
            catch (LmsProtocolException)
            {
                return; // cannot tell; carry on rather than wait forever
            }

            if (!scanning)
            {
                return;
            }

            SetStatus(new SyncStatus(SyncState.Syncing, Current is not null, "waiting for the server's scan to finish", Current?.Meta?.SyncedAt, null));
            await Task.Delay(_options.ScanPollInterval, ct).ConfigureAwait(false);
        }
    }

    private static async Task<List<T>> FetchAllAsync<T>(Func<int, int, CancellationToken, Task<Page<T>>> fetch, CancellationToken ct)
    {
        var all = new List<T>();
        await foreach (var item in Paging.EnumerateAsync(fetch, 2000, ct).ConfigureAwait(false))
        {
            all.Add(item);
        }

        return all;
    }

    private void Report(Progress stages, string stage, int done, int total)
    {
        stages.Set(stage, done, total);
        SetStatus(new SyncStatus(SyncState.Syncing, Current is not null, stages.Text, Current?.Meta?.SyncedAt, null));
    }

    private void Publish(Catalog? catalog)
    {
        lock (_gate)
        {
            _current = catalog;
        }

        CatalogChanged?.Invoke(catalog);
    }

    private void OpenStore(string uuid)
    {
        ICatalogStore store;
        lock (_gate)
        {
            if (_store is not null)
            {
                return;
            }

            _store = store = _options.StoreFactory(uuid);
        }

        if (Current is null && store.Load() is { } loaded)
        {
            Publish(loaded);
        }
    }

    private void Prompt(SyncPrompt prompt)
    {
        if (_deferred || _promptOutstanding)
        {
            return;
        }

        _promptOutstanding = true;
        PromptRequested?.Invoke(prompt);
    }

    private void SetStatus(SyncStatus status)
    {
        lock (_gate)
        {
            if (_status == status)
            {
                return;
            }

            _status = status;
        }

        StatusChanged?.Invoke(status);
    }

    private static CatalogAlbum ToCatalogAlbum(Album a) =>
        new(a.Id, a.Title, a.ArtistId, a.ArtistName ?? string.Empty, a.Year, a.ArtworkTrackId, a.IsCompilation, a.DiscCount);

    private static CatalogTrack ToCatalogTrack(Track t, int albumId) =>
        new(t.Id, albumId, t.Title, t.Artist, t.ArtistId, Math.Max(1, t.DiscNumber ?? 1), t.TrackNumber ?? 0, t.Duration, t.Genre, t.Url);

    /// <summary>Builds the progress text, for example <c>artists ✓ albums ✓ tracks 42%</c>.</summary>
    private sealed class Progress
    {
        private static readonly string[] Order = { "genres", "artists", "albums", "tracks" };
        private readonly Dictionary<string, (int Done, int Total)> _stages = new();

        public string Text => string.Join(
            ' ',
            Order.Where(o => o != "genres" || _stages.ContainsKey(o)).Select(o => !_stages.TryGetValue(o, out var s)
                ? $"{o} ·"
                : s.Done >= s.Total ? $"{o} ✓" : $"{o} {100 * s.Done / Math.Max(1, s.Total)}%"));

        public void Set(string stage, int done, int total) => _stages[stage] = (done, total);
    }
}
