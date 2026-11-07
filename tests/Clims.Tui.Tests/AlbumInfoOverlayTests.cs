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
    public void No_usable_address_means_no_format()
    {
        var (formats, lossless) = AlbumInfoOverlay.FormatsOf([null, "", "file:///music/noextension", "http://host/stream"]);
        Assert.Empty(formats);
        Assert.False(lossless);
    }
}
