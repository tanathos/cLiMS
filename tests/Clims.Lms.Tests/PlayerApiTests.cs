namespace Clims.Lms.Tests;

public class PlayerApiTests
{
    private const string Mac = "b8:27:eb:a9:77:8f";

    [Fact]
    public async Task Status_is_mapped_including_loosely_typed_fields()
    {
        var (client, handler) = TestClient.FromFixture("status");
        var before = DateTimeOffset.UtcNow;
        var s = await client.Player.GetStatusAsync(Mac, 0, 4);

        Assert.Equal(Mac, handler.Requests[0].PlayerId);
        Assert.Equal("piCorePlayer", s.Name);
        Assert.True(s.IsConnected);
        Assert.True(s.IsPoweredOn);
        Assert.Equal(PlayMode.Play, s.Mode);
        Assert.Equal(78.9679701118469, s.Elapsed.TotalSeconds, 6);
        Assert.Equal(TimeSpan.FromSeconds(421.906), s.Duration);
        Assert.Equal(70, s.Volume);
        Assert.Equal(RepeatMode.Off, s.Repeat);
        Assert.Equal(ShuffleMode.Off, s.Shuffle);
        Assert.True(s.CanSeek);
        Assert.Equal(3, s.CurrentIndex);            // "playlist_cur_index":"3"
        Assert.Equal(9, s.QueueLength);
        Assert.Equal(1793975126.07075, s.QueueTimestamp);
        Assert.True(s.ReceivedAt >= before);

        Assert.Equal(4, s.Items.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, s.Items.Select(i => i.Index));
        Assert.Equal("This Is Where It Falls Apart", s.CurrentTrack!.Title);
        Assert.Equal(true, s.Items[0].Track.IsLossless);
        Assert.Equal(16, s.Items[0].Track.SampleSize);
    }

    [Fact]
    public async Task Status_of_the_current_track_starts_the_window_with_a_dash()
    {
        var (client, handler) = TestClient.FromFixture("status_current");
        var s = await client.Player.GetStatusAsync(Mac);
        Assert.Equal(new[] { "status", "-", "1" }, handler.Requests[0].Command.Take(3));
        Assert.Single(s.Items);
        Assert.Equal(3, s.Items[0].Index);
    }

    [Fact]
    public void Elapsed_is_interpolated_while_playing_and_frozen_otherwise()
    {
        var at = DateTimeOffset.UtcNow;
        PlayerStatus Make(PlayMode mode) => new("p", true, true, mode, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(100), 50,
            RepeatMode.Off, ShuffleMode.Off, true, 0, 1, 1, Array.Empty<QueueItem>(), at);

        Assert.Equal(TimeSpan.FromSeconds(12.5), Make(PlayMode.Play).ElapsedAt(at.AddSeconds(2.5)));
        Assert.Equal(TimeSpan.FromSeconds(10), Make(PlayMode.Pause).ElapsedAt(at.AddSeconds(30)));
        Assert.Equal(TimeSpan.FromSeconds(10), Make(PlayMode.Stop).ElapsedAt(at.AddSeconds(30)));
        Assert.Equal(TimeSpan.FromSeconds(100), Make(PlayMode.Play).ElapsedAt(at.AddSeconds(500))); // clamped to the duration
        Assert.Equal(TimeSpan.FromSeconds(10), Make(PlayMode.Play).ElapsedAt(at.AddSeconds(-5)));    // clock skew
    }

    [Fact]
    public async Task Stopped_player_with_an_empty_queue_has_no_current_index()
    {
        var handler = new FakeHandler(_ => FakeHandler.Result("{\"player_name\":\"x\",\"mode\":\"stop\",\"playlist_tracks\":0,\"playlist_cur_index\":\"0\",\"time\":0}"));
        var s = await TestClient.Create(handler).Player.GetStatusAsync(Mac);
        Assert.Equal(PlayMode.Stop, s.Mode);
        Assert.Null(s.CurrentIndex);
        Assert.Null(s.CurrentTrack);
        Assert.Empty(s.Items);
    }

    public static IEnumerable<object[]> Commands() => new[]
    {
        new object[] { "play", (Func<IPlayerApi, Task>)(p => p.PlayAsync(Mac)), "play" },
        new object[] { "pause toggle", (Func<IPlayerApi, Task>)(p => p.PauseAsync(Mac)), "pause" },
        new object[] { "pause on", (Func<IPlayerApi, Task>)(p => p.PauseAsync(Mac, true)), "pause 1" },
        new object[] { "pause off", (Func<IPlayerApi, Task>)(p => p.PauseAsync(Mac, false)), "pause 0" },
        new object[] { "stop", (Func<IPlayerApi, Task>)(p => p.StopAsync(Mac)), "stop" },
        new object[] { "seek", (Func<IPlayerApi, Task>)(p => p.SeekAsync(Mac, TimeSpan.FromSeconds(83.25))), "time 83.25" },
        new object[] { "seek negative clamps", (Func<IPlayerApi, Task>)(p => p.SeekAsync(Mac, TimeSpan.FromSeconds(-4))), "time 0" },
        new object[] { "seek forward", (Func<IPlayerApi, Task>)(p => p.SeekByAsync(Mac, TimeSpan.FromSeconds(10))), "time +10" },
        new object[] { "seek back", (Func<IPlayerApi, Task>)(p => p.SeekByAsync(Mac, TimeSpan.FromSeconds(-10))), "time -10" },
        new object[] { "volume", (Func<IPlayerApi, Task>)(p => p.SetVolumeAsync(Mac, 35)), "mixer volume 35" },
        new object[] { "volume clamps high", (Func<IPlayerApi, Task>)(p => p.SetVolumeAsync(Mac, 140)), "mixer volume 100" },
        new object[] { "volume clamps low", (Func<IPlayerApi, Task>)(p => p.SetVolumeAsync(Mac, -3)), "mixer volume 0" },
        new object[] { "volume up", (Func<IPlayerApi, Task>)(p => p.AdjustVolumeAsync(Mac, 5)), "mixer volume +5" },
        new object[] { "volume down", (Func<IPlayerApi, Task>)(p => p.AdjustVolumeAsync(Mac, -5)), "mixer volume -5" },
        new object[] { "mute", (Func<IPlayerApi, Task>)(p => p.SetMutedAsync(Mac, true)), "mixer muting 1" },
        new object[] { "next", (Func<IPlayerApi, Task>)(p => p.NextAsync(Mac)), "playlist index +1" },
        new object[] { "previous", (Func<IPlayerApi, Task>)(p => p.PreviousAsync(Mac)), "playlist index -1" },
        new object[] { "jump", (Func<IPlayerApi, Task>)(p => p.JumpToAsync(Mac, 7)), "playlist index 7" },
        new object[] { "repeat queue", (Func<IPlayerApi, Task>)(p => p.SetRepeatAsync(Mac, RepeatMode.Queue)), "playlist repeat 2" },
        new object[] { "shuffle albums", (Func<IPlayerApi, Task>)(p => p.SetShuffleAsync(Mac, ShuffleMode.Albums)), "playlist shuffle 2" },
        new object[] { "power on", (Func<IPlayerApi, Task>)(p => p.SetPowerAsync(Mac, true)), "power 1" },
        new object[] { "power off", (Func<IPlayerApi, Task>)(p => p.SetPowerAsync(Mac, false)), "power 0" },
    };

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task Transport_commands_send_the_expected_words_to_the_right_player(string name, Func<IPlayerApi, Task> call, string expected)
    {
        _ = name;
        var (client, handler) = TestClient.Silent();
        await call(client.Player);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(expected, request.Text);
        Assert.Equal(Mac, request.PlayerId);
    }

    [Fact]
    public async Task Seek_uses_a_dot_decimal_separator_whatever_the_culture()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("it-IT");
            var (client, handler) = TestClient.Silent();
            await client.Player.SeekAsync(Mac, TimeSpan.FromSeconds(1.5));
            Assert.Equal("time 1.5", handler.Requests[0].Text);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }
    }
}
