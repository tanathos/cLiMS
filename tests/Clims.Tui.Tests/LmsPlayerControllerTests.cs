using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Clims.Lms;
using Clims.Tui.Data;
using Clims.Tui.Ui;
using Clims.Catalog;
using MusicCatalog = Clims.Catalog.Catalog;

namespace Clims.Tui.Tests;

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
