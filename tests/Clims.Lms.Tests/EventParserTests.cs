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
