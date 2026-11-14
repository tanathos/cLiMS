namespace Clims.Lms.Tests;

public class OnlineApiTests
{
    private const string Mac = "b8:27:eb:a9:77:8f";

    private static (IOnlineApi Api, FakeHandler Handler) Api(Func<RecordedRequest, string> reply)
    {
        var handler = new FakeHandler(r => FakeHandler.Result(reply(r)));
        return (new OnlineApi(TestClient.Create(handler).Rpc), handler);
    }

    [Fact]
    public async Task Radios_and_apps_are_listed_with_the_command_of_each_and_other_kinds_are_left_out()
    {
        var (api, handler) = Api(r => r.Command[0] == "radios"
            ? """{"count":2,"radioss_loop":[{"name":"Local Radio","type":"xmlbrowser","cmd":"local"},{"name":"Search","type":"xmlbrowser_search","cmd":"search"}]}"""
            : """{"count":3,"appss_loop":[{"name":"Spotty","type":"xmlbrowser","cmd":"spotty"},{"name":"Odd","type":"other","cmd":"odd"},{"name":"TIDAL","type":"xmlbrowser","cmd":"tidal"}]}""");

        var apps = await api.GetAppsAsync(Mac);

        Assert.Equal(new[] { "radios 0 200", "apps 0 200" }, handler.Requests.Select(r => r.Text));
        Assert.Equal(new[] { "Local Radio", "Search", "Spotty", "TIDAL" }, apps.Select(a => a.Name));
        Assert.Equal(new[] { true, true, false, false }, apps.Select(a => a.IsRadio));
        Assert.Equal(new[] { false, true, false, false }, apps.Select(a => a.IsSearch));
        Assert.Equal("tidal", apps[3].Command);
    }

    [Fact]
    public async Task Browsing_names_the_level_and_reads_the_items_whatever_the_array_is_called()
    {
        var (api, handler) = Api(_ =>
            """{"title":"Albums","count":2,"loop_loop":[{"id":"2.0","name":"Satellites by Mew","type":"playlist","isaudio":1,"hasitems":1},{"id":"2.1","name":"Please log in","type":"textarea","isaudio":0,"hasitems":1}]}""");

        var listing = await api.BrowseAsync(Mac, "spotty", "2", null, 0, 50);

        Assert.Equal("spotty items 0 50 item_id:2", handler.Requests[0].Text);
        Assert.Equal("Albums", listing.Title);
        Assert.Equal(2, listing.Total);
        Assert.True(listing.Items[0].IsAudio);
        Assert.True(listing.Items[0].HasItems);
        Assert.False(listing.Items[0].IsText);
        Assert.True(listing.Items[1].IsText);
    }

    [Fact]
    public async Task The_top_of_an_app_has_no_item_and_a_search_sends_its_text()
    {
        var (api, handler) = Api(_ => """{"count":0}""");

        var top = await api.BrowseAsync(Mac, "tidal", null, null, 0, 10);
        await api.BrowseAsync(Mac, "spotty", "0.0", "enslaved", 100, 100);

        Assert.Equal("tidal items 0 10", handler.Requests[0].Text);
        Assert.Empty(top.Items);
        Assert.False(top.HasMore);
        Assert.Equal("spotty items 100 100 item_id:0.0 search:enslaved", handler.Requests[1].Text);
    }

    [Fact]
    public async Task A_listing_longer_than_the_page_says_there_is_more()
    {
        var (api, _) = Api(_ => """{"count":250,"loop_loop":[{"id":"1","name":"One","isaudio":1,"hasitems":0}]}""");

        var listing = await api.BrowseAsync(Mac, "radionet", "1", null, 0, 1);

        Assert.True(listing.HasMore);
        Assert.Equal(250, listing.Total);
    }

    [Fact]
    public async Task Play_add_and_insert_use_the_playlist_command_of_the_app()
    {
        var (api, handler) = Api(_ => """{"count":1}""");

        await api.PlayAsync(Mac, "spotty", "2.0", OnlineAction.Play);
        await api.PlayAsync(Mac, "spotty", "2.0", OnlineAction.Add);
        await api.PlayAsync(Mac, "tidal", "1.2", OnlineAction.Insert);

        Assert.Equal(
            new[] { "spotty playlist play item_id:2.0", "spotty playlist add item_id:2.0", "tidal playlist insert item_id:1.2" },
            handler.Requests.Select(r => r.Text));
        Assert.All(handler.Requests, r => Assert.Equal(Mac, r.PlayerId));
    }
}
