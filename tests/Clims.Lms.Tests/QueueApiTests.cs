namespace Clims.Lms.Tests;

public class QueueApiTests
{
    private const string Mac = "b8:27:eb:a9:77:8f";

    private static IReadOnlyList<int> Ids(int n, int from = 1) => Enumerable.Range(from, n).ToList();

    private static int[] ParseIds(RecordedRequest r) =>
        r.Command.Single(w => w.StartsWith("track_id:", StringComparison.Ordinal))["track_id:".Length..].Split(',').Select(int.Parse).ToArray();

    [Fact]
    public async Task Add_sends_one_request_for_a_small_list()
    {
        var (client, handler) = TestClient.Silent();
        await client.Queue.AddTracksAsync(Mac, new[] { 5, 6, 7 });
        var r = Assert.Single(handler.Requests);
        Assert.Equal("playlistcontrol cmd:add track_id:5,6,7", r.Text);
        Assert.Equal(Mac, r.PlayerId);
    }

    [Fact]
    public async Task Add_splits_long_lists_into_chunks_of_at_most_400_in_order()
    {
        var (client, handler) = TestClient.Silent();
        await client.Queue.AddTracksAsync(Mac, Ids(950));

        Assert.Equal(new[] { 400, 400, 150 }, handler.Requests.Select(r => ParseIds(r).Length));
        Assert.Equal(Ids(950), handler.Requests.SelectMany(ParseIds));
        Assert.All(handler.Requests, r => Assert.Contains("cmd:add", r.Command));
    }

    [Fact]
    public async Task Request_bodies_of_a_full_chunk_stay_well_under_the_8kb_server_limit()
    {
        var (client, handler) = TestClient.Silent();
        await client.Queue.AddTracksAsync(Mac, Ids(400, 8_500_000)); // realistic 7-digit ids
        Assert.True(handler.Requests[0].Body.Length < 6000, $"body was {handler.Requests[0].Body.Length} bytes");
    }

    [Fact]
    public async Task The_chunk_size_comes_from_the_options()
    {
        var handler = new FakeHandler(_ => FakeHandler.Result("{}"));
        var client = TestClient.Create(handler, o => o.MaxIdsPerRequest = 100);
        await client.Queue.AddTracksAsync(Mac, Ids(250));
        Assert.Equal(new[] { 100, 100, 50 }, handler.Requests.Select(r => ParseIds(r).Length));
    }

    [Fact]
    public async Task Load_replaces_the_queue_with_the_first_chunk_then_appends_the_rest()
    {
        var (client, handler) = TestClient.Silent();
        await client.Queue.LoadTracksAsync(Mac, Ids(500), playIndex: 12);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("cmd:load", handler.Requests[0].Command);
        Assert.Contains("play_index:12", handler.Requests[0].Command);
        Assert.Contains("cmd:add", handler.Requests[1].Command);
        Assert.DoesNotContain(handler.Requests[1].Command, w => w.StartsWith("play_index", StringComparison.Ordinal));
        Assert.Equal(Ids(500), handler.Requests.SelectMany(ParseIds));
    }

    [Fact]
    public async Task Insert_next_sends_chunks_last_to_first_so_the_final_order_is_kept()
    {
        // Each insert lands right after the current track; sending chunk 2 first leaves chunk 1 in front of it.
        var (client, handler) = TestClient.Silent();
        await client.Queue.InsertTracksNextAsync(Mac, Ids(900));

        var sent = handler.Requests.Select(ParseIds).ToList();
        Assert.Equal(new[] { 100, 400, 400 }, sent.Select(s => s.Length));
        Assert.Equal(801, sent[0][0]);
        Assert.Equal(1, sent[^1][0]);
        Assert.All(handler.Requests, r => Assert.Contains("cmd:insert", r.Command));
    }

    [Fact]
    public async Task Album_commands_use_album_id()
    {
        var (client, handler) = TestClient.Silent();
        await client.Queue.LoadAlbumAsync(Mac, 853519, 2);
        await client.Queue.AddAlbumAsync(Mac, 853519);
        await client.Queue.InsertAlbumNextAsync(Mac, 853519);

        Assert.Equal("playlistcontrol cmd:load album_id:853519 play_index:2", handler.Requests[0].Text);
        Assert.Equal("playlistcontrol cmd:add album_id:853519", handler.Requests[1].Text);
        Assert.Equal("playlistcontrol cmd:insert album_id:853519", handler.Requests[2].Text);
    }

    [Fact]
    public async Task Remove_deletes_from_the_highest_index_down_and_ignores_duplicates()
    {
        var (client, handler) = TestClient.Silent();
        await client.Queue.RemoveAsync(Mac, new[] { 3, 9, 3, 1 });
        Assert.Equal(new[] { "playlist delete 9", "playlist delete 3", "playlist delete 1" }, handler.Requests.Select(r => r.Text));
    }

    [Fact]
    public async Task Move_and_clear()
    {
        var (client, handler) = TestClient.Silent();
        await client.Queue.MoveAsync(Mac, 4, 1);
        await client.Queue.ClearAsync(Mac);
        Assert.Equal(new[] { "playlist move 4 1", "playlist clear" }, handler.Requests.Select(r => r.Text));
    }

    [Fact]
    public async Task Empty_id_lists_send_nothing()
    {
        var (client, handler) = TestClient.Silent();
        await client.Queue.AddTracksAsync(Mac, Array.Empty<int>());
        await client.Queue.InsertTracksNextAsync(Mac, Array.Empty<int>());
        await client.Queue.LoadTracksAsync(Mac, Array.Empty<int>());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Queue_window_comes_from_status_with_the_server_total()
    {
        var (client, handler) = TestClient.FromFixture("status");
        var page = await client.Queue.GetQueueAsync(Mac, 0, 4);
        Assert.Equal(4, page.Items.Count);
        Assert.Equal(9, page.Total);
        Assert.True(page.HasMore);
        Assert.Equal("status 0 4", string.Join(' ', handler.Requests[0].Command.Take(3)));
    }

    [Fact]
    public async Task A_failing_chunk_stops_the_remaining_chunks()
    {
        var calls = 0;
        var handler = new FakeHandler(_ => ++calls == 2 ? FakeHandler.Json("") : FakeHandler.Result("{}"));
        var client = TestClient.Create(handler);
        await Assert.ThrowsAsync<LmsProtocolException>(() => client.Queue.AddTracksAsync(Mac, Ids(1000)));
        Assert.Equal(2, calls);
    }
}
