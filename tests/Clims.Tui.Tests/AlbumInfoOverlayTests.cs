using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public class AlbumInfoOverlayTests
{
    [Fact]
    public void Formats_come_from_the_file_extensions_without_repeats()
    {
        var (formats, lossless) = AlbumInfoOverlay.FormatsOf(["file:///music/a/01.flac", null, "file:///music/a/02.FLAC", "file:///music/a/03.flac?x=1"]);
        Assert.Equal(["FLAC"], formats);
        Assert.True(lossless);
    }

    [Fact]
    public void A_lossy_file_makes_the_album_not_lossless()
    {
        var (formats, lossless) = AlbumInfoOverlay.FormatsOf(["file:///m/1.flac", "file:///m/2.mp3"]);
        Assert.Equal(["FLAC", "MP3"], formats);
        Assert.False(lossless);
    }

    [Fact]
    public void Sections_are_as_long_as_their_tracks_with_a_constant_gap()
    {
        // 50 cells = 100 half cells; three gaps of 2 leave 94 for 10 minutes.
        var s = AlbumInfoOverlay.Sections([TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(6), TimeSpan.FromMinutes(2)], 50);
        Assert.Equal(4, s.Count);
        Assert.All(s.Zip(s.Skip(1)), p => Assert.Equal(2, p.Second.Start - p.First.End));
        Assert.Equal(0, s[0].Start);
        Assert.Equal(100, s[^1].End);
        // The 6-minute track is about six times the 1-minute one.
        Assert.InRange((s[2].End - s[2].Start) / (double)(s[0].End - s[0].Start), 5.0, 7.0);
    }

    [Fact]
    public void Too_many_tracks_shrink_the_gap_instead_of_overflowing()
    {
        var s = AlbumInfoOverlay.Sections(Enumerable.Repeat(TimeSpan.FromMinutes(3), 100).ToList(), 40);
        Assert.Equal(100, s.Count);
        Assert.All(s, x => Assert.InRange(x.End, 1, 80));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(83, "1:23")]
    [InlineData(3725, "1:02:05")]
    public void Time_is_minutes_and_seconds_or_hours(int seconds, string expected)
    {
        Assert.Equal(expected, AlbumInfoOverlay.FormatTime(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void No_usable_address_means_no_format()
    {
        var (formats, lossless) = AlbumInfoOverlay.FormatsOf([null, "", "file:///music/noextension", "http://host/stream"]);
        Assert.Empty(formats);
        Assert.False(lossless);
    }
}
