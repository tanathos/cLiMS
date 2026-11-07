using Clims.Catalog;
using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public class TrackLayoutTests
{
    private static List<CatalogTrack> Tracks(params int[] perDisc)
    {
        var list = new List<CatalogTrack>();
        for (var d = 0; d < perDisc.Length; d++)
        {
            for (var n = 1; n <= perDisc[d]; n++)
            {
                list.Add(new CatalogTrack(list.Count + 1, 1, $"T{d + 1}-{n}", null, null, d + 1, n, TimeSpan.FromMinutes(3), null));
            }
        }

        return list;
    }

    [Fact]
    public void Single_disc_has_no_header_and_fills_two_columns_downwards()
    {
        var layout = new AlbumsView.TrackLayout(Tracks(5));
        Assert.Empty(layout.Headers);
        Assert.Equal(3, layout.Rows);
        Assert.Equal((0, 2), layout.Slots[2]);
        Assert.Equal((1, 0), layout.Slots[3]);
        Assert.Equal(4, layout.TrackAt(1, 1));
        Assert.Equal(-1, layout.TrackAt(1, 2));
    }

    [Fact]
    public void Each_disc_gets_a_header_and_its_own_two_columns()
    {
        var layout = new AlbumsView.TrackLayout(Tracks(22, 20));
        Assert.Equal([(0, 1), (13, 2)], layout.Headers);
        Assert.Equal(1 + 11 + 1 + 1 + 10, layout.Rows);
        Assert.Equal((0, 1), layout.Slots[0]);
        Assert.Equal((1, 1), layout.Slots[11]);
        Assert.Equal((0, 14), layout.Slots[22]);
        Assert.Equal((1, 14), layout.Slots[32]);
    }
}
