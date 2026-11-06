using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Clims.Lms.Tests;

public class EventParserTests
{
    private const string Mac = "b8:27:eb:a9:77:8f";
    private const string EncodedMac = "b8%3A27%3Aeb%3Aa9%3A77%3A8f";

    [Fact]
    public void Newsong_gives_the_title_and_index()
    {
        var e = Assert.IsType<TrackChangedEvent>(LmsEventParser.Parse($"{EncodedMac} playlist newsong Satellites%20%28live%29 3"));
        Assert.Equal(Mac, e.PlayerId);
        Assert.Equal("Satellites (live)", e.Title);
        Assert.Equal(3, e.Index);
    }

    [Fact]
    public void Newsong_with_unicode_title_is_decoded_as_utf8()
    {
        var e = Assert.IsType<TrackChangedEvent>(LmsEventParser.Parse($"{EncodedMac} playlist newsong 10%20A%C3%B1os 0"));
        Assert.Equal("10 Años", e.Title);
    }

    [Fact]
    public void Open_gives_the_decoded_file_url()
    {
        var e = Assert.IsType<TrackOpenedEvent>(LmsEventParser.Parse($"{EncodedMac} playlist open file%3A%2F%2F%2Fmnt%2FMusic%2FMew%2F%2B%2520-%2F01%20-%20Satellites.mp3"));
        Assert.StartsWith("file:///mnt/Music/Mew/", e.Url);
    }

    [Theory]
    [InlineData("playlist stop", PlayMode.Stop)]
    [InlineData("playlist pause 1", PlayMode.Pause)]
    [InlineData("playlist pause 0", PlayMode.Play)]
    [InlineData("pause 1", PlayMode.Pause)]
    [InlineData("pause 0", PlayMode.Play)]
    [InlineData("play", PlayMode.Play)]
    [InlineData("stop", PlayMode.Stop)]
    public void Playback_state_changes(string rest, PlayMode expected)
    {
        var e = Assert.IsType<PlaybackStateEvent>(LmsEventParser.Parse($"{EncodedMac} {rest}"));
        Assert.Equal(expected, e.Mode);
    }

    [Fact]
    public void A_bare_pause_toggle_has_an_unknown_mode()
    {
        var e = Assert.IsType<PlaybackStateEvent>(LmsEventParser.Parse($"{EncodedMac} pause"));
        Assert.Null(e.Mode);
    }

    [Theory]
    [InlineData("mixer volume 45", 45)]
    [InlineData("mixer volume 250", 100)]
    public void Absolute_volume(string rest, int expected)
    {
        var e = Assert.IsType<VolumeChangedEvent>(LmsEventParser.Parse($"{EncodedMac} {rest}"));
        Assert.Equal(expected, e.Volume);
    }

    [Theory]
    [InlineData("mixer volume +5")]
    [InlineData("mixer volume -5")]
    public void Relative_volume_is_unknown_so_callers_requery(string rest)
    {
        var e = Assert.IsType<VolumeChangedEvent>(LmsEventParser.Parse($"{EncodedMac} {rest}"));
        Assert.Null(e.Volume);
    }

    [Fact]
    public void Time_power_and_connection_events()
    {
        Assert.Equal(TimeSpan.FromSeconds(78.5), Assert.IsType<PositionChangedEvent>(LmsEventParser.Parse($"{EncodedMac} time 78.5")).Position);
        Assert.True(Assert.IsType<PowerChangedEvent>(LmsEventParser.Parse($"{EncodedMac} power 1")).On);
        Assert.False(Assert.IsType<PowerChangedEvent>(LmsEventParser.Parse($"{EncodedMac} power 0")).On);
        Assert.False(Assert.IsType<PlayerConnectionEvent>(LmsEventParser.Parse($"{EncodedMac} client disconnect")).Connected);
        Assert.True(Assert.IsType<PlayerConnectionEvent>(LmsEventParser.Parse($"{EncodedMac} client reconnect")).Connected);
        Assert.True(Assert.IsType<PlayerConnectionEvent>(LmsEventParser.Parse($"{EncodedMac} client new")).Connected);
    }

    [Theory]
    [InlineData("playlist addtracks listRef ARRAY(0x1f3a)")]
    [InlineData("playlist inserttracks listRef ARRAY(0x1f3a) index:10")]
    [InlineData("playlist loadtracks listRef ARRAY(0x1f3a)")]
    [InlineData("playlist deletetracks listRef ARRAY(0x1f3a)")]
    [InlineData("playlist delete 4")]
    [InlineData("playlist move 3 1")]
    [InlineData("playlist clear")]
    [InlineData("playlist load_done")]
    [InlineData("playlistcontrol cmd:add track_id:1,2,3")]
    public void Queue_edits_become_a_queue_changed_hint_without_the_useless_payload(string rest)
    {
        var e = Assert.IsType<QueueChangedEvent>(LmsEventParser.Parse($"{EncodedMac} {rest}"));
        Assert.Equal(Mac, e.PlayerId);
    }

    [Theory]
    [InlineData("playlist repeat 1")]
    [InlineData("playlist shuffle 2")]
    public void Repeat_and_shuffle_are_mode_changes(string rest) =>
        Assert.IsType<ModeChangedEvent>(LmsEventParser.Parse($"{EncodedMac} {rest}"));

    [Fact]
    public void Server_events()
    {
        Assert.IsType<ScanFinishedEvent>(LmsEventParser.Parse("rescan done"));
        Assert.IsType<LibraryChangedEvent>(LmsEventParser.Parse("library changed"));
    }

    [Theory]
    [InlineData("listen 1")]
    [InlineData("version ?")]
    [InlineData("b8%3A27%3Aeb%3Aa9%3A77%3A8f material-skin client-registration")]
    [InlineData("b8%3A27%3Aeb%3Aa9%3A77%3A8f menustatus %5Bhome%5D")]
    [InlineData("b8%3A27%3Aeb%3Aa9%3A77%3A8f prefset server volume 40")]
    [InlineData("b8%3A27%3Aeb%3Aa9%3A77%3A8f prefset plugin.state preference ARRAY(0x1f3a)")]
    [InlineData("b8%3A27%3Aeb%3Aa9%3A77%3A8f playlist index 4")]
    [InlineData("b8%3A27%3Aeb%3Aa9%3A77%3A8f displaynotify")]
    [InlineData("b8%3A27%3Aeb%3Aa9%3A77%3A8f")]
    [InlineData("rescan start")]
    [InlineData("")]
    [InlineData("   ")]
    public void Noise_and_unknown_lines_are_dropped(string line) => Assert.Null(LmsEventParser.Parse(line));

    [Theory]
    [InlineData("b8:27:eb:a9:77:8f", true)]
    [InlineData("B8:27:EB:A9:77:8F", true)]
    [InlineData("192.168.1.21", true)]
    [InlineData("rescan", false)]
    [InlineData("playlist", false)]
    [InlineData("b8:27:eb", false)]
    public void Player_ids_are_macs_or_ip_addresses(string word, bool expected) =>
        Assert.Equal(expected, LmsEventParser.IsPlayerId(word));

    [Fact]
    public void Malformed_percent_escapes_do_not_throw() => Assert.Equal("100%", LmsEventParser.Decode("100%"));

    [Fact]
    public void Web_client_ids_that_are_ip_addresses_are_recognised()
    {
        var e = Assert.IsType<PlaybackStateEvent>(LmsEventParser.Parse("192.168.1.21 play"));
        Assert.Equal("192.168.1.21", e.PlayerId);
    }
}

public class LineDeduperTests
{
    [Fact]
    public void The_same_line_within_the_window_is_a_duplicate()
    {
        var d = new LineDeduper(TimeSpan.FromMilliseconds(250));
        var t = DateTimeOffset.UtcNow;
        Assert.False(d.IsDuplicate("a playlist newsong x 1", t));
        Assert.True(d.IsDuplicate("a playlist newsong x 1", t.AddMilliseconds(100)));
    }

    [Fact]
    public void The_same_line_after_the_window_is_new_again()
    {
        var d = new LineDeduper(TimeSpan.FromMilliseconds(250));
        var t = DateTimeOffset.UtcNow;
        d.IsDuplicate("x", t);
        Assert.False(d.IsDuplicate("x", t.AddMilliseconds(400)));
    }

    [Fact]
    public void Different_lines_are_independent()
    {
        var d = new LineDeduper(TimeSpan.FromSeconds(1));
        var t = DateTimeOffset.UtcNow;
        Assert.False(d.IsDuplicate("x", t));
        Assert.False(d.IsDuplicate("y", t));
    }

    [Fact]
    public void The_seen_table_does_not_grow_forever()
    {
        var d = new LineDeduper(TimeSpan.FromMilliseconds(10));
        var t = DateTimeOffset.UtcNow;
        for (var i = 0; i < 2000; i++)
        {
            d.IsDuplicate("line " + i, t.AddMilliseconds(i * 50));
        }

        Assert.False(d.IsDuplicate("line 0", t.AddSeconds(500)));
    }
}

public class StatusPollerDiffTests
{
    private static PlayerStatus Status(
        PlayMode mode = PlayMode.Play,
        int volume = 50,
        int? index = 0,
        int trackId = 1,
        double? timestamp = 1,
        int length = 3,
        bool power = true,
        RepeatMode repeat = RepeatMode.Off)
    {
        var item = new QueueItem(index ?? 0, new Track(trackId, "t" + trackId, null, null, null, null, null, null, TimeSpan.Zero, null, null, null, null, null, null, null, null, null, null, null));
        return new PlayerStatus("p", true, power, mode, TimeSpan.Zero, null, volume, repeat, ShuffleMode.Off, true, index, length, timestamp, new[] { item }, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void First_poll_yields_nothing() => Assert.Empty(StatusPoller.Diff("p", null, Status()));

    [Fact]
    public void Unchanged_status_yields_nothing() => Assert.Empty(StatusPoller.Diff("p", Status(), Status()));

    [Fact]
    public void Pausing_is_reported()
    {
        var e = Assert.IsType<PlaybackStateEvent>(Assert.Single(StatusPoller.Diff("p", Status(), Status(mode: PlayMode.Pause))));
        Assert.Equal(PlayMode.Pause, e.Mode);
    }

    [Fact]
    public void A_new_track_is_reported_with_its_index_and_title()
    {
        var e = Assert.IsType<TrackChangedEvent>(Assert.Single(StatusPoller.Diff("p", Status(), Status(index: 1, trackId: 2))));
        Assert.Equal(1, e.Index);
        Assert.Equal("t2", e.Title);
    }

    [Fact]
    public void Volume_queue_mode_and_power_changes_are_reported()
    {
        var events = StatusPoller.Diff("p", Status(), Status(volume: 60, timestamp: 2, length: 4, power: false, repeat: RepeatMode.Queue)).ToList();
        Assert.Contains(events, e => e is VolumeChangedEvent { Volume: 60 });
        Assert.Contains(events, e => e is QueueChangedEvent);
        Assert.Contains(events, e => e is ModeChangedEvent);
        Assert.Contains(events, e => e is PowerChangedEvent { On: false });
    }
}

public class CliEventStreamTests
{
    private const string EncodedMac = "b8%3A27%3Aeb%3Aa9%3A77%3A8f";

    private static CliEventStream Stream(int port) => new(new CliEventStreamOptions
    {
        Host = "127.0.0.1",
        Port = port,
        MinReconnectDelay = TimeSpan.FromMilliseconds(30),
        MaxReconnectDelay = TimeSpan.FromMilliseconds(60),
        DedupeWindow = TimeSpan.FromSeconds(2),
    });

    private static async Task<List<LmsEvent>> Collect(CliEventStream stream, Func<LmsEvent, bool> done, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        var events = new List<LmsEvent>();
        try
        {
            await foreach (var e in stream.ReadAllAsync(cts.Token))
            {
                events.Add(e);
                if (done(e))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("timed out waiting for events; got: " + string.Join(", ", events.Select(e => e.GetType().Name)));
        }

        return events;
    }

    [Fact]
    public async Task Subscribes_with_listen_1_and_delivers_parsed_deduplicated_events()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        string? firstLine = null;

        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            firstLine = await reader.ReadLineAsync();
            var lines = new[]
            {
                "listen 1",                                                              // echo of our command: noise
                $"{EncodedMac} material-skin client-registration",                       // noise
                $"{EncodedMac} playlist newsong Satellites 3",
                $"{EncodedMac} playlist newsong Satellites 3",                           // duplicate
                $"{EncodedMac} mixer volume 40",
                $"{EncodedMac} prefset server volume 40",                                // noise
                "rescan done",
                "rescan done",                                                           // fires twice, even with no changes
            };
            var bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
            await stream.WriteAsync(bytes);
            await Task.Delay(TimeSpan.FromSeconds(3));
        });

        try
        {
            var events = await Collect(Stream(port), e => e is ScanFinishedEvent);

            Assert.Equal("listen 1", firstLine);
            Assert.Collection(
                events,
                e => Assert.True(Assert.IsType<EventStreamStateEvent>(e).Connected),
                e => Assert.Equal("Satellites", Assert.IsType<TrackChangedEvent>(e).Title),
                e => Assert.Equal(40, Assert.IsType<VolumeChangedEvent>(e).Volume),
                e => Assert.IsType<ScanFinishedEvent>(e));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Reconnects_after_the_server_drops_the_connection()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var server = Task.Run(async () =>
        {
            for (var session = 0; session < 2; session++)
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.UTF8);
                await reader.ReadLineAsync();
                var line = session == 0 ? $"{EncodedMac} play\n" : $"{EncodedMac} stop\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(line));
                if (session == 0)
                {
                    await Task.Delay(100); // then hang up
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
        });

        try
        {
            var events = await Collect(Stream(port), e => e is PlaybackStateEvent { Mode: PlayMode.Stop });

            var kinds = events.Select(e => e switch
            {
                EventStreamStateEvent s => s.Connected ? "up" : "down",
                PlaybackStateEvent p => p.Mode.ToString()!,
                _ => e.GetType().Name,
            });
            Assert.Equal(new[] { "up", "Play", "down", "up", "Stop" }, kinds);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Reports_offline_and_keeps_retrying_while_nothing_listens()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var events = await Collect(Stream(port), e => e is EventStreamStateEvent { Connected: false }, TimeSpan.FromSeconds(10));
        var down = Assert.IsType<EventStreamStateEvent>(Assert.Single(events));
        Assert.False(down.Connected);
        Assert.False(string.IsNullOrEmpty(down.Reason));
    }

    [Fact]
    public async Task Stops_cleanly_when_the_consumer_cancels()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await Task.Delay(TimeSpan.FromSeconds(5));
        });

        try
        {
            using var cts = new CancellationTokenSource();
            var seen = new List<LmsEvent>();
            var run = Task.Run(async () =>
            {
                await foreach (var e in Stream(port).ReadAllAsync(cts.Token))
                {
                    seen.Add(e);
                    cts.Cancel();
                }
            });

            var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(run, finished);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            Assert.NotEmpty(seen);
        }
        finally
        {
            listener.Stop();
        }
    }
}
