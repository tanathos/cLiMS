using Clims.Catalog;
using Clims.Tui.Data;
using Clims.Tui.Settings;
using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public class RecentTabTests
{
    [Fact]
    public void Albums_are_listed_in_the_servers_order_skipping_unknown_and_repeated_ids()
    {
        var catalog = DemoCatalog.Create();
        var a = catalog.Albums[0].Id;
        var b = catalog.Albums[1].Id;
        var c = catalog.Albums[2].Id;

        var list = RecentTab.Resolve([c, 999_999, a, c, b], catalog);

        Assert.Equal([c, a, b], list.Select(x => x.Id));
    }

    [Fact]
    public void No_ids_means_no_albums()
    {
        Assert.Empty(RecentTab.Resolve([], DemoCatalog.Create()));
    }

    [Fact]
    public async Task The_demo_session_lists_its_newest_albums_first()
    {
        using var session = new Session(null, new SettingsStore(Path.Combine(Path.GetTempPath(), "clims-demo-" + Guid.NewGuid().ToString("N"))), null);
        var ids = await session.RecentAlbums(5, CancellationToken.None);
        Assert.Equal(5, ids.Count);
        Assert.Equal(ids.OrderByDescending(i => i), ids);
    }
}
