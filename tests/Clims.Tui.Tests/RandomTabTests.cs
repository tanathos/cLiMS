using Clims.Catalog;
using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public class RandomTabTests
{
    private static List<CatalogAlbum> Albums(int n) =>
        Enumerable.Range(1, n).Select(i => new CatalogAlbum(i, $"A{i}", null, "Artist", 2000, null, false, 1)).ToList();

    [Fact]
    public void Pick_returns_the_requested_number_of_distinct_albums()
    {
        var picked = RandomTab.Pick(Albums(200), 50, new Random(1));

        Assert.Equal(50, picked.Count);
        Assert.Equal(50, picked.Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public void Pick_returns_everything_when_the_library_is_small()
    {
        Assert.Equal(7, RandomTab.Pick(Albums(7), 50, new Random(1)).Count);
        Assert.Empty(RandomTab.Pick(Albums(0), 50, new Random(1)));
    }

    [Fact]
    public void Pick_differs_between_draws()
    {
        var random = new Random(2);
        var all = Albums(200);

        var first = RandomTab.Pick(all, 50, random).Select(a => a.Id);
        var second = RandomTab.Pick(all, 50, random).Select(a => a.Id);

        Assert.NotEqual(first, second);
    }
}
