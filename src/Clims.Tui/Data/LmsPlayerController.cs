using System.Threading.Channels;
using Clims.Lms;
using Clims.Tui.Ui;

namespace Clims.Tui.Data;

/// <summary>
/// The real player: status polled about once a second (and at once after every command or server event), the elapsed time
/// interpolated between polls, the queue re-read when the server says it changed. Commands run one at a time in the order
/// they were given, so a "play next" followed by an "add" cannot overtake each other. Playback is never stopped to
/// advance: queue edits use the in-place commands of <see cref="IQueueApi"/>; only <see cref="Play"/> and loading a saved
/// queue replace the queue (and so restart the stream).
/// </summary>
public sealed class LmsPlayerController : IPlayerController, IDisposable
{
    private static readonly TrackTags QueueTags =
        TrackTags.Artist | TrackTags.ArtistId | TrackTags.Album | TrackTags.AlbumId | TrackTags.TrackNumber | TrackTags.Disc
        | TrackTags.Duration | TrackTags.Url | TrackTags.ArtworkTrackId | TrackTags.Year;

    private readonly ILmsClient _client;
    private readonly Func<Clims.Catalog.Catalog?> _catalog;
    private readonly LmsPlayerOptions _options;
    private readonly object _gate = new();
    private readonly Channel<(string What, Func<CancellationToken, Task> Run)> _commands = Channel.CreateUnbounded<(string, Func<CancellationToken, Task>)>();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private Task? _worker;
    private string? _playerId;
    private string _name = "Player";
    private PlayerStatus? _status;
    private NowPlaying? _current;
    private IReadOnlyList<QueueEntry> _queue = Array.Empty<QueueEntry>();
    private double? _queueStamp;
    private int _queueLength = -1;
    private bool _connected;
    private string? _problem = "Looking for the player…";
    private int _inFlight;          // commands given but not yet answered by the server
    private int _editGeneration;    // bumped by every optimistic local change

    /// <summary>Creates the controller. Nothing is contacted until <see cref="Start"/>.</summary>
    public LmsPlayerController(ILmsClient client, Func<Clims.Catalog.Catalog?> catalog, LmsPlayerOptions? options = null)
    {
        _client = client;
        _catalog = catalog;
        _options = options ?? new LmsPlayerOptions();
        _playerId = _options.PlayerId;
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public event Action<string>? Notice;

    /// <inheritdoc />
    public string PlayerName
    {
        get
        {
            lock (_gate)
            {
                return _name;
            }
        }
    }

    /// <summary>The id of the player being controlled, once found.</summary>
    public string? PlayerId
    {
        get
        {
            lock (_gate)
            {
                return _playerId;
            }
        }
    }

    /// <inheritdoc />
    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return _status?.Mode == PlayMode.Play;
            }
        }
    }

    /// <inheritdoc />
    public int Volume
    {
        get
        {
            lock (_gate)
            {
                return _status?.Volume ?? 0;
            }
        }
    }

    /// <inheritdoc />
    public NowPlaying? Current
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
    public TimeSpan Elapsed
    {
        get
        {
            lock (_gate)
            {
                return _status?.ElapsedAt(_options.Now()) ?? TimeSpan.Zero;
            }
        }
    }

    /// <inheritdoc />
    public bool Connected
    {
        get
        {
            lock (_gate)
            {
                return _connected;
            }
        }
    }

    /// <inheritdoc />
    public string? Problem
    {
        get
        {
            lock (_gate)
            {
                return _problem;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<QueueEntry> Queue
    {
        get
        {
            lock (_gate)
            {
                return _queue;
            }
        }
    }

    /// <inheritdoc />
    public int QueueIndex
    {
        get
        {
            lock (_gate)
            {
                return _status?.CurrentIndex ?? -1;
            }
        }
    }

    /// <summary>Starts polling and the command worker.</summary>
    public void Start()
    {
        _worker ??= Task.Run(() => WorkAsync(_stop.Token));
        _loop ??= Task.Run(() => PollLoopAsync(_stop.Token));
    }

    /// <summary>Server events are hints: whatever they say, the status is read again at once.</summary>
    public async Task FollowEventsAsync(ILmsEventStream events, CancellationToken cancellationToken)
    {
        await foreach (var e in events.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var mine = _playerId;
            var relevant = e switch
            {
                TrackChangedEvent t => t.PlayerId == mine,
                PlaybackStateEvent s => s.PlayerId == mine,
                VolumeChangedEvent v => v.PlayerId == mine,
                PositionChangedEvent p => p.PlayerId == mine,
                QueueChangedEvent q => q.PlayerId == mine,
                ModeChangedEvent m => m.PlayerId == mine,
                PowerChangedEvent w => w.PlayerId == mine,
                PlayerConnectionEvent c => c.PlayerId == mine,
                EventStreamStateEvent { Connected: true } => true,
                _ => false,
            };
            if (relevant)
            {
                Poke();
            }
        }
    }

    /// <summary>Reads the status now instead of at the next tick.</summary>
    public void Poke()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // A refresh is already pending.
        }
    }

    /// <summary>Waits until every command given so far has been sent (tests, and a clean shutdown).</summary>
    public async Task FlushAsync()
    {
        var done = new TaskCompletionSource();
        Post("flush", _ =>
        {
            done.SetResult();
            return Task.CompletedTask;
        });
        await done.Task.ConfigureAwait(false);
    }

    /// <summary>Stops polling and drops commands that have not been sent.</summary>
    public void Dispose()
    {
        _stop.Cancel();
        _commands.Writer.TryComplete();
    }

    // ----- commands -------------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public void TogglePlay()
    {
        var mode = _status?.Mode;
        Edit(s => s with { Mode = mode == PlayMode.Play ? PlayMode.Pause : PlayMode.Play, Elapsed = s.ElapsedAt(_options.Now()), ReceivedAt = _options.Now() });
        Do("Play/pause", (id, ct) => mode == PlayMode.Stop ? _client.Player.PlayAsync(id, ct) : _client.Player.PauseAsync(id, mode == PlayMode.Play, ct));
    }

    /// <inheritdoc />
    public void Next() => Do("Next", (id, ct) => _client.Player.NextAsync(id, ct));

    /// <inheritdoc />
    public void Previous()
    {
        if (Elapsed > _options.RestartThreshold)
        {
            SeekTo(0);
        }
        else
        {
            Do("Previous", (id, ct) => _client.Player.PreviousAsync(id, ct));
        }
    }

    /// <inheritdoc />
    public void Seek(TimeSpan delta)
    {
        if (_current is null)
        {
            return;
        }

        var target = Elapsed + delta;
        if (_current.Track.Duration > TimeSpan.Zero)
        {
            target = TimeSpan.FromSeconds(Math.Clamp(target.TotalSeconds, 0, Math.Max(0, _current.Track.Duration.TotalSeconds - 0.5)));
        }

        MoveTo(target < TimeSpan.Zero ? TimeSpan.Zero : target);
    }

    /// <inheritdoc />
    public void SeekTo(double fraction)
    {
        if (_current is not { } now)
        {
            return;
        }

        MoveTo(TimeSpan.FromSeconds(Math.Clamp(fraction, 0, 1) * now.Track.Duration.TotalSeconds));
    }

    /// <inheritdoc />
    public void SetVolume(int volume)
    {
        volume = Math.Clamp(volume, 0, 100);
        Edit(s => s with { Volume = volume });
        Do("Volume", (id, ct) => _client.Player.SetVolumeAsync(id, volume, ct));
    }

    /// <inheritdoc />
    public void Play(Clims.Catalog.CatalogAlbum album, IReadOnlyList<Clims.Catalog.CatalogTrack> tracks, int index)
    {
        if (tracks.Count == 0)
        {
            return;
        }

        var ids = tracks.Select(t => t.Id).ToList();
        var start = Math.Clamp(index, 0, ids.Count - 1);
        Do("Play", (id, ct) => _client.Queue.LoadTracksAsync(id, ids, start, ct));
    }

    /// <inheritdoc />
    public void Enqueue(QueueAction action, Clims.Catalog.CatalogAlbum album, IReadOnlyList<Clims.Catalog.CatalogTrack> tracks)
    {
        if (tracks.Count == 0)
        {
            return;
        }

        var ids = tracks.Select(t => t.Id).ToList();
        var what = action == QueueAction.Add ? "Add to queue" : "Play next";
        Do(
            what,
            async (id, ct) =>
            {
                var before = _status;
                if (action == QueueAction.Add)
                {
                    await _client.Queue.AddTracksAsync(id, ids, ct).ConfigureAwait(false);
                }
                else
                {
                    await _client.Queue.InsertTracksNextAsync(id, ids, ct).ConfigureAwait(false);
                }

                await EnsurePlaybackContinuesAsync(id, before, ct).ConfigureAwait(false);
                Notify(action == QueueAction.Add ? $"Added {Describe(ids.Count, album)} to the queue." : $"{Describe(ids.Count, album)} will play next.");
            });
    }

    /// <inheritdoc />
    public void PlayTracks(IReadOnlyList<(Clims.Catalog.CatalogAlbum Album, Clims.Catalog.CatalogTrack Track)> tracks, bool replace)
    {
        if (tracks.Count == 0)
        {
            return;
        }

        var ids = tracks.Select(t => t.Track.Id).ToList();
        Do(
            replace ? "Play" : "Add to queue",
            async (id, ct) =>
            {
                var before = _status;
                if (replace)
                {
                    await _client.Queue.LoadTracksAsync(id, ids, 0, ct).ConfigureAwait(false);
                    Notify($"Playing {ids.Count} tracks.");
                    return;
                }

                await _client.Queue.AddTracksAsync(id, ids, ct).ConfigureAwait(false);
                await EnsurePlaybackContinuesAsync(id, before, ct).ConfigureAwait(false);
                Notify($"Added {ids.Count} tracks to the queue.");
            });
    }

    /// <inheritdoc />
    public void JumpTo(int index) => Do("Jump", (id, ct) => _client.Player.JumpToAsync(id, index, ct));

    /// <inheritdoc />
    public void RemoveFromQueue(int index) => Do("Remove", (id, ct) => _client.Queue.RemoveAsync(id, new[] { index }, ct));

    /// <inheritdoc />
    public void MoveInQueue(int from, int to)
    {
        if (from == to || from < 0 || to < 0)
        {
            return;
        }

        Do("Move", (id, ct) => _client.Queue.MoveAsync(id, from, to, ct));
    }

    /// <inheritdoc />
    public void ClearQueue() => Do("Clear", (id, ct) => _client.Queue.ClearAsync(id, ct));

    /// <inheritdoc />
    public void PlaySaved(SavedQueue queue, bool replace)
    {
        Do(
            replace ? "Load queue" : "Add queue",
            async (id, ct) =>
            {
                var ids = new int?[queue.Tracks.Count];
                using var slots = new SemaphoreSlim(3);
                await Task.WhenAll(queue.Tracks.Select(async (t, i) =>
                {
                    await slots.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        ids[i] = (await _client.Library.GetTrackByUrlAsync(t.Url, TrackTags.None, ct).ConfigureAwait(false))?.Id;
                    }
                    finally
                    {
                        slots.Release();
                    }
                })).ConfigureAwait(false);

                var found = ids.Where(i => i is not null).Select(i => i!.Value).ToList();
                var missing = ids.Length - found.Count;
                if (found.Count == 0)
                {
                    Notify($"\"{queue.Name}\": none of its {ids.Length} tracks are in the library any more.");
                    return;
                }

                var before = _status;
                if (replace)
                {
                    await _client.Queue.LoadTracksAsync(id, found, 0, ct).ConfigureAwait(false);
                }
                else
                {
                    await _client.Queue.AddTracksAsync(id, found, ct).ConfigureAwait(false);
                    await EnsurePlaybackContinuesAsync(id, before, ct).ConfigureAwait(false);
                }

                Notify(missing == 0
                    ? $"{(replace ? "Playing" : "Added")} \"{queue.Name}\" ({found.Count} tracks)."
                    : $"{(replace ? "Playing" : "Added")} \"{queue.Name}\": {found.Count} tracks, {missing} no longer in the library.");
            });
    }

    /// <inheritdoc />
    public void Tick(TimeSpan dt)
    {
        // Real time is read from the clock; nothing to advance.
    }

    private static string Describe(int count, Clims.Catalog.CatalogAlbum album) =>
        count == 1 ? "1 track" : $"{count} tracks of \"{album.Title}\"";

    /// <summary>
    /// Appending to a queue whose last track already ended must not leave the player silent: if it stopped with tracks
    /// left, start the next one (a gap is fine then, there was nothing playing anyway).
    /// </summary>
    private async Task EnsurePlaybackContinuesAsync(string id, PlayerStatus? before, CancellationToken ct)
    {
        if (before?.Mode != PlayMode.Play)
        {
            return;
        }

        var after = await _client.Player.GetStatusAsync(id, null, 1, TrackTags.None, ct).ConfigureAwait(false);
        if (after.Mode == PlayMode.Stop && after.CurrentIndex is { } i && i + 1 < after.QueueLength)
        {
            await _client.Player.NextAsync(id, ct).ConfigureAwait(false);
        }
    }

    private void MoveTo(TimeSpan target)
    {
        Edit(s => s with { Elapsed = target, ReceivedAt = _options.Now() });
        Do("Seek", (id, ct) => _client.Player.SeekAsync(id, target, ct));
    }

    private void Notify(string text) => Notice?.Invoke(text);

    /// <summary>Changes the local status at once so the footer reacts before the server has answered.</summary>
    private void Edit(Func<PlayerStatus, PlayerStatus> change)
    {
        lock (_gate)
        {
            if (_status is null)
            {
                return;
            }

            _status = change(_status);
            _editGeneration++;
        }

        Changed?.Invoke();
    }

    private void Do(string what, Func<string, CancellationToken, Task> command)
    {
        var id = PlayerId;
        if (id is null)
        {
            Notify(Problem ?? "No player to control yet.");
            return;
        }

        Post(what, ct => command(id, ct));
    }

    private void Post(string what, Func<CancellationToken, Task> run)
    {
        Interlocked.Increment(ref _inFlight);
        if (!_commands.Writer.TryWrite((what, run)))
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private async Task WorkAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var (what, run) in _commands.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await run(ct).ConfigureAwait(false);
                }
                catch (LmsException ex)
                {
                    Notify($"{what} failed: {ex.Message}");
                }
                finally
                {
                    Interlocked.Decrement(ref _inFlight);
                }

                Poke();                 // after the decrement: this poll's answer is allowed to replace the optimistic state
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    // ----- status ---------------------------------------------------------------------------------------------------

    private async Task PollLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var ok = await PollOnceAsync(ct).ConfigureAwait(false);
                await _wake.WaitAsync(ok ? _options.PollInterval : _options.RetryInterval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    /// <summary>One status read. Returns false when the server or the player could not be reached.</summary>
    internal async Task<bool> PollOnceAsync(CancellationToken ct)
    {
        try
        {
            if (_playerId is null && !await FindPlayerAsync(ct).ConfigureAwait(false))
            {
                return false;
            }

            var id = _playerId!;
            int generation;
            lock (_gate)
            {
                generation = _editGeneration;
            }

            var busyAtStart = Volatile.Read(ref _inFlight);
            var status = await _client.Player.GetStatusAsync(id, null, 1, QueueTags, ct).ConfigureAwait(false);
            var queue = _queue;
            var queueRead = false;
            if (status.QueueLength != _queueLength || status.QueueTimestamp != _queueStamp)
            {
                var page = await _client.Queue.GetQueueAsync(id, 0, _options.MaxQueue, QueueTags, ct).ConfigureAwait(false);
                queue = page.Items.Select(i => ToEntry(i.Index, i.Track)).ToList();
                queueRead = true;
            }

            lock (_gate)
            {
                // The answer may predate a command that is still on its way (or a local edit made while it travelled):
                // showing it would flick the volume or the playhead back. The poll that follows the command is applied.
                if (busyAtStart > 0 || Volatile.Read(ref _inFlight) > 0 || generation != _editGeneration)
                {
                    return true;
                }

                if (queueRead)
                {
                    _queueLength = status.QueueLength;     // remembered only when the queue was really taken over
                    _queueStamp = status.QueueTimestamp;
                }

                _status = status;
                _name = string.IsNullOrEmpty(status.Name) ? _name : status.Name;
                _queue = queue;
                _current = status.CurrentTrack is { } t ? new NowPlaying(AlbumOf(t), ToTrack(t)) : null;
                _connected = status.IsConnected;
                _problem = status.IsConnected ? null : $"{_name} is not connected to the server.";
            }

            Changed?.Invoke();
            return true;
        }
        catch (LmsException ex)
        {
            lock (_gate)
            {
                _connected = false;
                _problem = "The server cannot be reached. " + ex.Message;
            }

            Changed?.Invoke();
            return false;
        }
    }

    private async Task<bool> FindPlayerAsync(CancellationToken ct)
    {
        var players = await _client.Server.GetPlayersAsync(ct).ConfigureAwait(false);
        var pick = players.FirstOrDefault(p => p.IsConnected && p.IsHardwarePlayer)
            ?? players.FirstOrDefault(p => p.IsConnected)
            ?? players.FirstOrDefault();
        if (pick is null)
        {
            lock (_gate)
            {
                _connected = false;
                _problem = "No player found on the server.";
            }

            Changed?.Invoke();
            return false;
        }

        lock (_gate)
        {
            _playerId = pick.PlayerId;
            _name = pick.Name;
        }

        return true;
    }

    private QueueEntry ToEntry(int index, Track t) => new(index, ToTrack(t), AlbumOf(t));

    private static Clims.Catalog.CatalogTrack ToTrack(Track t) =>
        new(t.Id, t.AlbumId ?? 0, t.Title, t.Artist, t.ArtistId, t.DiscNumber ?? 1, t.TrackNumber ?? 0, t.Duration, t.Genre, t.Url);

    /// <summary>The catalogue's album when it has it; otherwise one made from what the track says about its album.</summary>
    private Clims.Catalog.CatalogAlbum AlbumOf(Track t)
    {
        var known = t.AlbumId is { } albumId ? _catalog()?.Album(albumId) : null;
        return known ?? new Clims.Catalog.CatalogAlbum(
            t.AlbumId ?? 0,
            t.Album ?? string.Empty,
            t.ArtistId,
            t.Artist ?? string.Empty,
            t.Year ?? 0,
            t.ArtworkTrackId,
            t.IsCompilation ?? false,
            null);
    }
}
