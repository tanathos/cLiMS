using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms.Tests;

public class ArtworkAndAudioUrlTests
{
    private static readonly Uri Base = new("http://192.168.1.20:9000/");

    [Fact]
    public void Cover_url_for_an_artwork_id() =>
        Assert.Equal("http://192.168.1.20:9000/music/2836a7e5/cover_96x96_o.jpg", ArtworkUrl.Cover(Base, "2836a7e5", 96, 96).ToString());

    [Fact]
    public void Cover_url_for_a_track_id_and_png() =>
        Assert.Equal("http://192.168.1.20:9000/music/8547903/cover_300x300_o.png", ArtworkUrl.Cover(Base, 8547903, 300, 300, ArtworkFormat.Png).ToString());

    [Fact]
    public void Cover_url_respects_a_base_path()
    {
        var url = ArtworkUrl.Cover(new Uri("http://host/lms/"), "ab", 48, 48);
        Assert.Equal("http://host/lms/music/ab/cover_48x48_o.jpg", url.ToString());
    }

    [Fact]
    public void Cover_url_rejects_blank_ids() =>
        Assert.Throws<ArgumentException>(() => ArtworkUrl.Cover(Base, " ", 48, 48));

    [Fact]
    public void Download_url() =>
        Assert.Equal("http://192.168.1.20:9000/music/8547903/download", AudioUrl.Download(Base, 8547903).ToString());
}
