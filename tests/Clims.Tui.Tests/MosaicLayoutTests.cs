using Clims.Tui.Settings;
using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public class MosaicLayoutTests
{
    // Four units across, and a view high enough for the largest tile.
    private const int Width = 4 * 16 + 3 * 2;
    private const int Rows = 40;

    [Fact]
    public void More_listens_make_a_larger_tile_up_to_three_units()
    {
        Assert.Equal(new[] { 1, 2, 3, 3 }, new[] { 1, 2, 3, 12 }.Select(MosaicLayout.SpanFor));
    }

    [Fact]
    public void Tiles_are_square_on_screen_with_cells_twice_as_high_as_wide()
    {
        var tiles = MosaicLayout.Pack([1, 2, 3], Width, Rows).Tiles;

        Assert.All(tiles, t => Assert.Equal(t.Cols, t.Rows * 2));
    }

    [Fact]
    public void Small_tiles_fill_the_holes_next_to_large_ones()
    {
        var layout = MosaicLayout.Pack([3, 1, 1, 1, 1], Width, Rows);

        Assert.Equal(new[] { (0, 0), (54, 0), (54, 9), (54, 18), (0, 27) }, layout.Tiles.Select(t => (t.X, t.Y)));
    }

    [Fact]
    public void Tiles_never_overlap_or_leave_the_width()
    {
        var random = new Random(3);
        var spans = Enumerable.Range(0, 120).Select(_ => random.Next(1, 4)).ToList();

        var tiles = MosaicLayout.Pack(spans, Width, Rows).Tiles;

        Assert.Equal(spans, tiles.Select(t => t.Span));
        Assert.All(tiles, t => Assert.True(t.X >= 0 && t.Right <= Width));
        Assert.All(tiles, a => Assert.All(tiles.Where(b => b != a), b => Assert.False(a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom)));
    }

    [Theory]
    [InlineData(Width, 40, 3)]
    [InlineData(Width, 20, 2)]
    [InlineData(Width, 5, 1)]
    [InlineData(34, 40, 2)]
    [InlineData(10, 40, 1)]
    public void A_small_view_caps_the_size(int width, int rows, int largest)
    {
        Assert.Equal(largest, MosaicLayout.Pack([3], width, rows).Tiles[0].Span);
    }

    [Fact]
    public void An_empty_list_makes_an_empty_mosaic()
    {
        var layout = MosaicLayout.Pack([], Width, Rows);

        Assert.Empty(layout.Tiles);
        Assert.Equal(0, layout.Height);
        Assert.Equal(-1, layout.Nearest(0, 0));
    }

    [Fact]
    public void A_cell_finds_its_tile_and_gaps_find_none()
    {
        var layout = MosaicLayout.Pack([2, 1], Width, Rows);

        Assert.Equal(0, layout.At(0, 0));
        Assert.Equal(1, layout.At(36, 0));
        Assert.Equal(-1, layout.At(34, 0));
        Assert.Equal(-1, layout.At(36, 8));
    }

    [Fact]
    public void Arrows_go_to_the_tile_on_that_side()
    {
        // 0 is large on the left, 1 to 3 are stacked right of it, 4 is under it.
        var layout = MosaicLayout.Pack([3, 1, 1, 1, 1], Width, Rows);

        Assert.Equal(1, layout.Neighbor(0, 1, 0));
        Assert.Equal(0, layout.Neighbor(2, -1, 0));
        Assert.Equal(2, layout.Neighbor(1, 0, 1));
        Assert.Equal(4, layout.Neighbor(0, 0, 1));
        Assert.Equal(0, layout.Neighbor(4, 0, -1));
        Assert.Equal(0, layout.Neighbor(0, -1, 0));
    }

    [Fact]
    public void Played_albums_default_to_the_grid()
    {
        Assert.Equal(PlayedLayout.Grid, new HistorySettings().AlbumsView);
    }
}
