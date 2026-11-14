namespace Clims.Lms.Tests;

public class RemoteTrackTests
{
    private const string Mac = "b8:27:eb:a9:77:8f";

    private static async Task<Track> Current(string item)
    {
        var handler = new FakeHandler(_ => FakeHandler.Result(
            "{\"mode\":\"play\",\"playlist_cur_index\":\"0\",\"playlist_tracks\":1,\"playlist_loop\":[" + item + "]}"));
        var status = await TestClient.Create(handler).Player.GetStatusAsync(Mac, 0, 1);
        return status.CurrentTrack!;
    }

    [Fact]
    public async Task A_radio_stream_knows_its_station_and_takes_its_cover_from_the_server_image_proxy()
    {
        var t = await Current("""{"playlist index":0,"id":"-267","remote":1,"remote_title":"RADIO BOB! Metal","artist":"Deserted Fear","title":"Sins from the Past","artwork_url":"/imageproxy/https%3A%2F%2Fx.test%2Fa.png/image.png"}""");

        Assert.True(t.IsRemote);
        Assert.Equal("RADIO BOB! Metal", t.RemoteTitle);
        Assert.Equal("/imageproxy/https%3A%2F%2Fx.test%2Fa.png/image.png", t.ArtworkTrackId);
    }

    [Fact]
    public async Task An_absolute_artwork_address_goes_through_the_image_proxy_and_a_library_track_is_not_remote()
    {
        var radio = await Current("""{"playlist index":0,"id":"-1","remote":1,"title":"x","artwork_url":"https://x.test/a b.png"}""");
        var file = await Current("""{"playlist index":0,"id":"5","title":"y","artwork_track_id":"abc"}""");

        Assert.Equal("/imageproxy/" + Uri.EscapeDataString("https://x.test/a b.png") + "/image.png", radio.ArtworkTrackId);
        Assert.False(file.IsRemote);
        Assert.Equal("abc", file.ArtworkTrackId);
    }

    [Fact]
    public async Task A_stream_is_recognised_by_its_negative_id_even_without_the_remote_flag()
    {
        var t = await Current("""{"playlist index":0,"id":"-267417448","title":"Layers of Time","artist":"Lacuna Coil","artwork_url":"/imageproxy/x/image.jpg"}""");

        Assert.True(t.IsRemote);
        Assert.Equal("/imageproxy/x/image.jpg", t.ArtworkTrackId);
    }
}
