using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Clims.Lms;
using Clims.Tui.Data;
using Clims.Tui.Ui;
using Clims.Catalog;
using MusicCatalog = Clims.Catalog.Catalog;

namespace Clims.Tui.Tests;

internal static class TestLog
{
    [ModuleInitializer]
    internal static void Redirect() =>
        CrashLog.Path = Path.Combine(Path.GetTempPath(), "clims-tui-tests.log");
}

/// <summary>An in-memory LMS player: state to poll, a log of every command, switches for outages and failures.</summary>
internal sealed class FakePlayerLms : ILmsClient, IPlayerApi, IQueueApi, IServerApi, ILibraryApi
{
    private readonly object _gate = new();

    public List<PlayerInfo> Players { get; } = new()
    {
        new PlayerInfo("aa:bb", "Pi", "squeezelite", "1.2.3.4", true, false, true, true),
    };

    public PlayMode Mode { get; set; } = PlayMode.Play;

    public TimeSpan Elapsed { get; set; } = TimeSpan.FromSeconds(30);

    public int Volume { get; set; } = 50;

    public int CurrentIndex { get; set; } = 1;

    public double QueueStamp { get; set; } = 1;

    public List<Track> Queue { get; } = new();

    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    public Exception? Outage { get; set; }

    public Func<string, Exception?>? FailCommand { get; set; }

    /// <summary>Every command, as text: "Pause(True)", "Seek(40)", "Add(1,2)".</summary>
    public List<string> Commands { get; } = new();

    public int StatusCalls { get; private set; }

    public int QueueReads { get; private set; }

    public Dictionary<string, int> UrlToId { get; } = new();

    public Uri BaseUri { get; } = new("http://fake/");

    public ILmsRpcClient Rpc => throw new NotSupportedException();

    public ILibraryApi Library => this;

    public IPlayerApi Player => this;

    IQueueApi ILmsClient.Queue => this;

    public IServerApi Server => this;

    public static Track MakeTrack(int id, string title = "Song", int albumId = 7, string? url = null) =>
        new(id, title, "Artist", 3, "Album", albumId, id, 1, TimeSpan.FromMinutes(4), 1999, url ?? $"file:///music/{id}.flac", null, null, "art" + id, null, null, null, null, null, null);

    public void Fill(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            Queue.Add(MakeTrack(i, $"Song {i}"));
        }
    }

    private void Check(string command)
    {
        if (Outage is not null)
        {
            throw Outage;
        }

        lock (_gate)
        {
            Commands.Add(command);
        }

        if (FailCommand?.Invoke(command) is { } failure)
        {
            throw failure;
        }
    }

    // IServerApi
    public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult("9.1.1");

    public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<PlayerInfo>> GetPlayersAsync(CancellationToken cancellationToken = default)
    {
        if (Outage is not null)
        {
            throw Outage;
        }

        return Task.FromResult<IReadOnlyList<PlayerInfo>>(Players.ToList());
    }

    public Task<ScanState> GetScanStateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> GetTrackCountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> CanAsync(IReadOnlyList<string> command, CancellationToken cancellationToken = default) => Task.FromResult(true);

    // IPlayerApi
    public Task<PlayerStatus> GetStatusAsync(string playerId, int? start = null, int count = 1, TrackTags tags = TrackTags.List, CancellationToken cancellationToken = default)
    {
        if (Outage is not null)
        {
            throw Outage;
        }

        StatusCalls++;
        var items = Queue.Select((t, i) => new QueueItem(i, t)).Where(q => q.Index == CurrentIndex).ToList();
        return Task.FromResult(new PlayerStatus(
            "Pi", true, true, Mode, Elapsed, TimeSpan.FromMinutes(4), Volume, RepeatMode.Off, ShuffleMode.Off, true,
            Queue.Count == 0 ? null : CurrentIndex, Queue.Count, QueueStamp, items, Now()));
    }

    public Task PlayAsync(string playerId, CancellationToken cancellationToken = default)
    {
        Check("Play");
        Mode = PlayMode.Play;
        return Task.CompletedTask;
    }

    public Task PauseAsync(string playerId, bool? pause = null, CancellationToken cancellationToken = default)
    {
        Check($"Pause({pause})");
        Mode = pause == true ? PlayMode.Pause : PlayMode.Play;
        return Task.CompletedTask;
    }

    public Task StopAsync(string playerId, CancellationToken cancellationToken = default)
    {
        Check("Stop");
        return Task.CompletedTask;
    }

    public Task SeekAsync(string playerId, TimeSpan position, CancellationToken cancellationToken = default)
    {
        Check($"Seek({position.TotalSeconds:0})");
        Elapsed = position;
        return Task.CompletedTask;
    }

    public Task SeekByAsync(string playerId, TimeSpan delta, CancellationToken cancellationToken = default)
    {
        Check($"SeekBy({delta.TotalSeconds:0})");
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(string playerId, int volume, CancellationToken cancellationToken = default)
    {
        Check($"Volume({volume})");
        Volume = volume;
        return Task.CompletedTask;
    }

    public Task AdjustVolumeAsync(string playerId, int delta, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetMutedAsync(string playerId, bool muted, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NextAsync(string playerId, CancellationToken cancellationToken = default)
    {
        Check("Next");
        return Task.CompletedTask;
    }

    public Task PreviousAsync(string playerId, CancellationToken cancellationToken = default)
    {
        Check("Previous");
        return Task.CompletedTask;
    }

    public Task JumpToAsync(string playerId, int index, CancellationToken cancellationToken = default)
    {
        Check($"Jump({index})");
        CurrentIndex = index;
        return Task.CompletedTask;
    }

    public Task SetRepeatAsync(string playerId, RepeatMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetShuffleAsync(string playerId, ShuffleMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetPowerAsync(string playerId, bool on, CancellationToken cancellationToken = default) => Task.CompletedTask;

    // IQueueApi
    public Task<Page<QueueItem>> GetQueueAsync(string playerId, int start = 0, int count = 100, TrackTags tags = TrackTags.List, CancellationToken cancellationToken = default)
    {
        QueueReads++;
        var items = Queue.Select((t, i) => new QueueItem(i, t)).Skip(start).Take(count).ToList();
        return Task.FromResult(new Page<QueueItem>(items, Queue.Count, start));
    }

    public Task LoadTracksAsync(string playerId, IReadOnlyList<int> trackIds, int playIndex = 0, CancellationToken cancellationToken = default)
    {
        Check($"Load({string.Join(',', trackIds)}@{playIndex})");
        return Task.CompletedTask;
    }

    public Task AddTracksAsync(string playerId, IReadOnlyList<int> trackIds, CancellationToken cancellationToken = default)
    {
        Check($"Add({string.Join(',', trackIds)})");
        OnAdd?.Invoke();
        return Task.CompletedTask;
    }

    public Action? OnAdd { get; set; }

    public Task InsertTracksNextAsync(string playerId, IReadOnlyList<int> trackIds, CancellationToken cancellationToken = default)
    {
        Check($"Next({string.Join(',', trackIds)})");
        return Task.CompletedTask;
    }

    public Task LoadAlbumAsync(string playerId, int albumId, int playIndex = 0, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task AddAlbumAsync(string playerId, int albumId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task InsertAlbumNextAsync(string playerId, int albumId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RemoveAsync(string playerId, IReadOnlyCollection<int> indices, CancellationToken cancellationToken = default)
    {
        Check($"Remove({string.Join(',', indices)})");
        return Task.CompletedTask;
    }

    public Task MoveAsync(string playerId, int fromIndex, int toIndex, CancellationToken cancellationToken = default)
    {
        Check($"Move({fromIndex}->{toIndex})");
        return Task.CompletedTask;
    }

    public Task ClearAsync(string playerId, CancellationToken cancellationToken = default)
    {
        Check("Clear");
        return Task.CompletedTask;
    }

    // ILibraryApi
    public Task<Page<Artist>> GetArtistsAsync(ArtistQuery? query = null, int start = 0, int count = 5000, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Page<Album>> GetAlbumsAsync(AlbumQuery? query = null, int start = 0, int count = 5000, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Page<Track>> GetTracksAsync(TrackQuery query, int start = 0, int count = 500, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Page<Genre>> GetGenresAsync(int start = 0, int count = 5000, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<SearchResults> SearchAsync(string term, int count = 10, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Track?> GetTrackAsync(int trackId, TrackTags tags = TrackTags.All, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Track?> GetTrackByUrlAsync(string url, TrackTags tags = TrackTags.All, CancellationToken cancellationToken = default) =>
        Task.FromResult(UrlToId.TryGetValue(url, out var id) ? MakeTrack(id) : null);

    public Task<Page<SavedPlaylist>> GetPlaylistsAsync(int start = 0, int count = 500, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class FakeEventStream : ILmsEventStream
{
    private readonly Channel<LmsEvent> _channel = Channel.CreateUnbounded<LmsEvent>();

    public void Push(LmsEvent e) => _channel.Writer.TryWrite(e);

    public IAsyncEnumerable<LmsEvent> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public class LmsPlayerControllerTests
{
    private static readonly LmsPlayerOptions Quiet = new() { PollInterval = TimeSpan.FromHours(1), RetryInterval = TimeSpan.FromHours(1) };

    private static async Task WaitAsync(Func<bool> condition)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "timed out");
            await Task.Delay(5);
        }
    }

    private static async Task<(LmsPlayerController Player, FakePlayerLms Lms)> StartedAsync(Action<FakePlayerLms>? setup = null, LmsPlayerOptions? options = null, Func<MusicCatalog?>? catalog = null)
    {
        var lms = new FakePlayerLms();
        lms.Fill(3);
        setup?.Invoke(lms);
        var player = new LmsPlayerController(lms, catalog ?? (() => null), options ?? Quiet);
        player.Start();
        await WaitAsync(() => player.Current is not null || player.Problem is { Length: > 0 } p && !p.StartsWith("Looking"));
        return (player, lms);
    }

    [Fact]
    public async Task The_status_becomes_now_playing_and_the_queue()
    {
        var (player, lms) = await StartedAsync();
        using var _ = player;

        Assert.True(player.Connected);
        Assert.Null(player.Problem);
        Assert.Equal("Pi", player.PlayerName);
        Assert.True(player.IsPlaying);
        Assert.Equal(50, player.Volume);
        Assert.Equal(1, player.QueueIndex);
        Assert.Equal("Song 2", player.Current!.Track.Title);
        Assert.Equal("Album", player.Current.Album.Title);          // built from the track: the catalogue is not loaded
        Assert.Equal("art2", player.Current.Album.ArtworkId);
        Assert.Equal(new[] { "Song 1", "Song 2", "Song 3" }, player.Queue.Select(e => e.Track.Title));
        Assert.Equal("file:///music/1.flac", player.Queue[0].Track.Url);
        Assert.Equal(1, lms.QueueReads);
    }

    [Fact]
    public async Task The_albums_of_the_catalogue_are_used_when_it_knows_them()
    {
        var album = new CatalogAlbum(7, "Catalogue Album", 3, "Artist", 1999, "cat-art", false, 1);
        var catalog = new MusicCatalog(Array.Empty<CatalogGenre>(), new[] { new CatalogArtist(3, "Artist") }, new[] { album }, Array.Empty<CatalogTrack>(), null);
        var (player, _) = await StartedAsync(catalog: () => catalog);
        using var _ = player;

        Assert.Equal(album, player.Current!.Album);
    }

    [Fact]
    public async Task Elapsed_time_runs_between_polls_and_stops_when_paused()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = new LmsPlayerOptions { PollInterval = TimeSpan.FromHours(1), Now = () => now };
        var (player, lms) = await StartedAsync(l => l.Now = () => now, clock);
        using var _ = player;

        Assert.Equal(30, player.Elapsed.TotalSeconds, 1);
        now += TimeSpan.FromSeconds(5);
        Assert.Equal(35, player.Elapsed.TotalSeconds, 1);

        lms.Mode = PlayMode.Pause;
        player.Poke();
        await WaitAsync(() => !player.IsPlaying);
        var frozen = player.Elapsed;
        now += TimeSpan.FromSeconds(10);
        Assert.Equal(frozen, player.Elapsed);
    }

    [Fact]
    public async Task Without_any_player_the_problem_says_so_and_commands_give_a_notice()
    {
        var (player, _) = await StartedAsync(l => l.Players.Clear());
        using var _ = player;
        var notices = new List<string>();
        player.Notice += notices.Add;

        Assert.False(player.Connected);
        Assert.Equal("No player found on the server.", player.Problem);
        player.Next();
        player.TogglePlay();

        Assert.Equal(2, notices.Count);
        Assert.Contains("No player found", notices[0]);
    }

    [Fact]
    public async Task An_unreachable_server_is_reported_and_forgotten_once_it_is_back()
    {
        var (player, lms) = await StartedAsync();
        using var _ = player;

        lms.Outage = new LmsConnectionException("down");
        player.Poke();
        await WaitAsync(() => !player.Connected);
        Assert.Contains("cannot be reached", player.Problem);
        Assert.NotNull(player.Current);                                  // the last known state stays on screen

        lms.Outage = null;
        player.Poke();
        await WaitAsync(() => player.Connected);
        Assert.Null(player.Problem);
    }

    [Fact]
    public async Task A_hardware_player_is_preferred_and_a_configured_id_wins()
    {
        var (auto, _) = await StartedAsync(l =>
        {
            l.Players.Clear();
            l.Players.Add(new PlayerInfo("web", "Browser", null, null, true, false, true, false));
            l.Players.Add(new PlayerInfo("hw", "Pi", null, null, true, false, true, true));
        });
        using var _ = auto;
        Assert.Equal("hw", auto.PlayerId);

        var (chosen, _) = await StartedAsync(
            l =>
            {
                l.Players.Add(new PlayerInfo("other", "Other", null, null, true, false, true, true));
            },
            new LmsPlayerOptions { PlayerId = "other", PollInterval = TimeSpan.FromHours(1) });
        using var __ = chosen;
        Assert.Equal("other", chosen.PlayerId);
    }

    [Theory]
    [InlineData(PlayMode.Play, "Pause(True)")]
    [InlineData(PlayMode.Pause, "Pause(False)")]
    [InlineData(PlayMode.Stop, "Play")]
    public async Task Play_pause_does_the_right_thing_for_each_state(PlayMode mode, string expected)
    {
        var (player, lms) = await StartedAsync(l => l.Mode = mode);
        using var _ = player;

        player.TogglePlay();
        await player.FlushAsync();

        Assert.Equal(new[] { expected }, lms.Commands);
    }

    [Fact]
    public async Task Transport_commands_reach_the_server()
    {
        var (player, lms) = await StartedAsync();
        using var _ = player;

        player.Next();
        player.Seek(TimeSpan.FromSeconds(10));
        player.SeekTo(0.5);
        player.SetVolume(80);
        player.JumpTo(2);
        player.RemoveFromQueue(0);
        player.MoveInQueue(2, 1);
        player.ClearQueue();
        await player.FlushAsync();

        Assert.Equal(new[] { "Next", "Seek(40)", "Seek(120)", "Volume(80)", "Jump(2)", "Remove(0)", "Move(2->1)", "Clear" }, lms.Commands);
        Assert.Equal(80, player.Volume);                                  // shown at once
    }

    [Fact]
    public async Task Previous_restarts_a_track_that_has_played_a_while_and_goes_back_early_in_it()
    {
        var (player, lms) = await StartedAsync();
        using var _ = player;
        player.Previous();                                                // 30 s in
        lms.Elapsed = TimeSpan.FromSeconds(1);
        player.Poke();
        await player.FlushAsync();
        await WaitAsync(() => player.Elapsed < TimeSpan.FromSeconds(3));
        player.Previous();
        await player.FlushAsync();

        Assert.Equal(new[] { "Seek(0)", "Previous" }, lms.Commands);
    }

    [Fact]
    public async Task Seeking_never_goes_before_the_start_or_past_the_end()
    {
        var (player, lms) = await StartedAsync();
        using var _ = player;

        player.Seek(TimeSpan.FromMinutes(-5));
        player.Seek(TimeSpan.FromMinutes(30));
        await player.FlushAsync();

        Assert.Equal("Seek(0)", lms.Commands[0]);
        Assert.True(int.Parse(lms.Commands[1][5..^1]) is > 200 and <= 240);   // just inside the 4-minute track
    }

    [Fact]
    public async Task Playing_an_album_loads_it_and_enqueueing_adds_in_place()
    {
        var (player, lms) = await StartedAsync();
        using var _ = player;
        var album = new CatalogAlbum(7, "A", 3, "Artist", 1999, null, false, 1);
        var tracks = new[] { new CatalogTrack(11, 7, "x", null, null, 1, 1, TimeSpan.Zero, null), new CatalogTrack(12, 7, "y", null, null, 1, 2, TimeSpan.Zero, null) };
        var notices = new List<string>();
        player.Notice += notices.Add;

        player.Play(album, tracks, 1);
        player.Enqueue(QueueAction.PlayNext, album, tracks);
        player.Enqueue(QueueAction.Add, album, tracks.Take(1).ToList());
        await player.FlushAsync();

        Assert.Equal(new[] { "Load(11,12@1)", "Next(11,12)", "Add(11)" }, lms.Commands);   // order preserved
        Assert.Contains(notices, n => n.Contains("will play next"));
        Assert.Contains(notices, n => n.StartsWith("Added 1 track"));
    }

    [Fact]
    public async Task Adding_to_a_queue_that_just_ran_dry_starts_the_next_track()
    {
        var (player, lms) = await StartedAsync(l => l.Mode = PlayMode.Play);
        using var _ = player;
        lms.OnAdd = () => lms.Mode = PlayMode.Stop;                       // the server stopped when the last track ended
        var album = new CatalogAlbum(7, "A", 3, "Artist", 1999, null, false, 1);

        player.Enqueue(QueueAction.Add, album, new[] { new CatalogTrack(11, 7, "x", null, null, 1, 1, TimeSpan.Zero, null) });
        await player.FlushAsync();

        Assert.Equal(new[] { "Add(11)", "Next" }, lms.Commands);
    }

    [Fact]
    public async Task Adding_while_paused_does_not_start_playback()
    {
        var (player, lms) = await StartedAsync(l => l.Mode = PlayMode.Pause);
        using var _ = player;
        var album = new CatalogAlbum(7, "A", 3, "Artist", 1999, null, false, 1);

        player.Enqueue(QueueAction.Add, album, new[] { new CatalogTrack(11, 7, "x", null, null, 1, 1, TimeSpan.Zero, null) });
        await player.FlushAsync();

        Assert.Equal(new[] { "Add(11)" }, lms.Commands);
    }

    [Fact]
    public async Task A_failed_command_becomes_a_notice_and_the_next_command_still_runs()
    {
        var (player, lms) = await StartedAsync(l => l.FailCommand = c => c == "Next" ? new LmsProtocolException("closed") : null);
        using var _ = player;
        var notices = new List<string>();
        player.Notice += notices.Add;

        player.Next();
        player.SetVolume(10);
        await player.FlushAsync();

        Assert.Contains(notices, n => n.StartsWith("Next failed"));
        Assert.Contains("Volume(10)", lms.Commands);
    }

    [Fact]
    public async Task The_queue_is_read_again_only_when_it_changed()
    {
        var (player, lms) = await StartedAsync();
        using var _ = player;
        Assert.Equal(1, lms.QueueReads);

        player.Poke();
        await WaitAsync(() => lms.StatusCalls >= 2);
        Assert.Equal(1, lms.QueueReads);                                   // same timestamp and length

        lms.QueueStamp = 2;
        player.Poke();
        await WaitAsync(() => lms.QueueReads == 2);

        lms.Queue.Add(FakePlayerLms.MakeTrack(4, "Song 4"));
        player.Poke();
        await WaitAsync(() => player.Queue.Count == 4);
    }

    [Fact]
    public async Task Server_events_for_this_player_refresh_the_status_at_once()
    {
        var (player, lms) = await StartedAsync();
        using var _ = player;
        var events = new FakeEventStream();
        using var cts = new CancellationTokenSource();
        var follow = player.FollowEventsAsync(events, cts.Token);
        var before = lms.StatusCalls;

        events.Push(new VolumeChangedEvent("someone-else", 5));            // not ours: ignored
        events.Push(new TrackChangedEvent("aa:bb", 2, "Song 3"));
        await WaitAsync(() => lms.StatusCalls > before);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => follow);
    }

    [Fact]
    public async Task A_saved_queue_is_resolved_by_url_and_missing_files_are_reported()
    {
        var (player, lms) = await StartedAsync(l =>
        {
            l.UrlToId["file:///a.flac"] = 101;
            l.UrlToId["file:///c.flac"] = 103;                               // b is gone from the library
        });
        using var _ = player;
        var notices = new List<string>();
        player.Notice += notices.Add;
        var saved = new SavedQueue("Mix", DateTimeOffset.Now, new[]
        {
            new SavedTrack("file:///a.flac", "A", null, null, 1),
            new SavedTrack("file:///b.flac", "B", null, null, 1),
            new SavedTrack("file:///c.flac", "C", null, null, 1),
        });

        player.PlaySaved(saved, replace: true);
        player.PlaySaved(saved, replace: false);
        await player.FlushAsync();

        Assert.Equal(new[] { "Load(101,103@0)", "Add(101,103)" }, lms.Commands);
        Assert.Contains(notices, n => n.Contains("Playing \"Mix\": 2 tracks, 1 no longer"));
    }

    [Fact]
    public async Task A_saved_queue_with_nothing_left_loads_nothing()
    {
        var (player, lms) = await StartedAsync();
        using var _ = player;
        var notices = new List<string>();
        player.Notice += notices.Add;

        player.PlaySaved(new SavedQueue("Old", DateTimeOffset.Now, new[] { new SavedTrack("file:///gone.flac", "G", null, null, 1) }), true);
        await player.FlushAsync();

        Assert.Empty(lms.Commands);
        Assert.Contains(notices, n => n.Contains("none of its 1 tracks"));
    }
}

public class SavedQueueStoreTests
{
    private static string NewPath() => Path.Combine(Path.GetTempPath(), "clims-queues-" + Guid.NewGuid().ToString("N"), "queues.json");

    private static SavedTrack T(string name) => new($"file:///{name}.flac", name, "Artist", "Album", 200);

    [Fact]
    public void Queues_survive_a_restart_newest_first()
    {
        var path = NewPath();
        var store = new SavedQueueStore(path);
        store.Save("Old", new[] { T("a") }, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        store.Save("New", new[] { T("b"), T("c") }, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        var again = new SavedQueueStore(path).List();

        Assert.Equal(new[] { "New", "Old" }, again.Select(q => q.Name));
        Assert.Equal(new[] { "file:///b.flac", "file:///c.flac" }, again[0].Tracks.Select(t => t.Url));
    }

    [Fact]
    public void Saving_under_an_existing_name_replaces_it_ignoring_case()
    {
        var store = new SavedQueueStore(NewPath());
        store.Save("Mix", new[] { T("a") });
        store.Save("mix", new[] { T("b"), T("c") });

        var queue = Assert.Single(store.List());
        Assert.Equal(2, queue.Tracks.Count);
    }

    [Fact]
    public void Delete_removes_one_and_reports_whether_it_existed()
    {
        var store = new SavedQueueStore(NewPath());
        store.Save("Mix", new[] { T("a") });

        Assert.True(store.Delete("MIX"));
        Assert.False(store.Delete("Mix"));
        Assert.Empty(store.List());
    }

    [Fact]
    public void A_damaged_file_is_moved_aside_and_the_list_starts_empty()
    {
        var path = NewPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        var store = new SavedQueueStore(path);

        Assert.Empty(store.List());
        Assert.True(File.Exists(path + ".bad"));
        store.Save("Fresh", new[] { T("a") });
        Assert.Single(store.List());
    }

    [Fact]
    public void A_name_is_required_and_trimmed()
    {
        var store = new SavedQueueStore(NewPath());
        Assert.Throws<ArgumentException>(() => store.Save("   ", new[] { T("a") }));
        store.Save("  Spaced  ", new[] { T("a") });
        Assert.Equal("Spaced", store.List()[0].Name);
    }

    [Fact]
    public void Unicode_names_and_titles_roundtrip()
    {
        var store = new SavedQueueStore(NewPath());
        store.Save("Música ♫ 日本", new[] { new SavedTrack("file:///é.flac", "Pantéon – 'quoted' \"x\"", "Motörhead", null, 1.5) });
        var q = Assert.Single(store.List());
        Assert.Equal("Música ♫ 日本", q.Name);
        Assert.Equal("Pantéon – 'quoted' \"x\"", q.Tracks[0].Title);
    }
}
