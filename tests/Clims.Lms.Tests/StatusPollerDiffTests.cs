using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Clims.Lms.Tests;

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
