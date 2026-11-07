namespace Clims.Lms.Tests;

public class ServerApiTests
{
    [Fact]
    public async Task Server_status_is_mapped()
    {
        var (client, _) = TestClient.FromFixture("serverstatus");
        var s = await client.Server.GetStatusAsync();

        Assert.Equal("9.1.1", s.Version);
        Assert.Equal("7c1f32ca-018f-41d3-b018-c4587df043f7", s.Uuid);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793939356), s.LastScan);
        Assert.Equal(2008, s.AlbumCount);
        Assert.Equal(1004, s.ArtistCount);
        Assert.Equal(195, s.GenreCount);
        Assert.Equal(19948, s.TrackCount);
        Assert.Equal(6116359.01, s.TotalDuration.TotalSeconds, 1);

        Assert.Equal(2, s.Players.Count);
        var pi = s.Players[0];
        Assert.Equal("b8:27:eb:a9:77:8f", pi.PlayerId);
        Assert.Equal("piCorePlayer", pi.Name);
        Assert.Equal("squeezelite", pi.Model);
        Assert.True(pi.IsConnected);
        Assert.True(pi.IsPlaying);
        Assert.True(pi.IsPoweredOn);
        Assert.True(pi.IsHardwarePlayer);

        var web = s.Players[1];
        Assert.Equal("192.168.1.21", web.PlayerId);
        Assert.False(web.IsHardwarePlayer);
        Assert.False(web.IsConnected);
    }

    [Fact]
    public async Task Version_total_count_and_scan_state()
    {
        var (v, _) = TestClient.FromFixture("version");
        Assert.Equal("9.1.1", await v.Server.GetVersionAsync());

        var (t, _) = TestClient.FromFixture("info_total_songs");
        Assert.Equal(19948, await t.Server.GetTrackCountAsync());

        var (s, _) = TestClient.FromFixture("rescanprogress");
        var scan = await s.Server.GetScanStateAsync();
        Assert.False(scan.IsScanning);
        Assert.Empty(scan.Details);
    }

    [Fact]
    public async Task Scan_in_progress_exposes_its_details()
    {
        var handler = new FakeHandler(_ => FakeHandler.Result("{\"rescan\":1,\"steps\":\"directory,mp3tags\",\"directory\":\"100\",\"mp3tags\":\"43\"}"));
        var scan = await TestClient.Create(handler).Server.GetScanStateAsync();
        Assert.True(scan.IsScanning);
        Assert.Equal("43", scan.Details["mp3tags"]);
        Assert.False(scan.Details.ContainsKey("rescan"));
    }

    [Fact]
    public async Task Can_asks_whether_a_command_exists()
    {
        var (client, handler) = TestClient.FromFixture("can_playlistcontrol");
        Assert.True(await client.Server.CanAsync(new[] { "playlistcontrol" }));
        Assert.Equal("can playlistcontrol ?", handler.Requests[0].Text);

        var no = new FakeHandler(_ => FakeHandler.Result("{\"_can\":0}"));
        Assert.False(await TestClient.Create(no).Server.CanAsync(new[] { "playlist", "bogus" }));
    }

    [Fact]
    public async Task Players_are_listed()
    {
        var (client, handler) = TestClient.FromFixture("players");
        var players = await client.Server.GetPlayersAsync();
        Assert.Equal(2, players.Count);
        Assert.Equal("players", handler.Requests[0].Command[0]);
    }
}
