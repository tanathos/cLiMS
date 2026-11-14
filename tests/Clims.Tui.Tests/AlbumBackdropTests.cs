using Clims.Tui.Settings;
using Clims.Tui.Ui;
using Terminal.Gui.Drawing;
using Xunit;

namespace Clims.Tui.Tests;

public class AlbumBackdropTests
{
    private static CoverImage Image(Color left, Color right)
    {
        var pixels = new Color[8, 8];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                pixels[x, y] = x < 4 ? left : right;
            }
        }

        return CoverImage.FromPixels(pixels, "test");
    }

    [Fact]
    public void NoCoverMeansNoBackdrop() => Assert.Null(AlbumBackdrop.Make(null, GridBackdrop.Tint, 40, 10));

    [Fact]
    public void TintIsOneColourBetweenTheThemeAndTheCover()
    {
        var red = Theme.Rgb(255, 0, 0);
        var backdrop = AlbumBackdrop.Make(Image(red, red), GridBackdrop.Tint, 40, 10)!;
        Assert.Equal(backdrop(0, 0), backdrop(39, 9));
        Assert.Equal(Theme.Mix(Theme.Bg, red, AlbumBackdrop.Strength), backdrop(5, 5));
    }

    [Fact]
    public void PixellatedFollowsTheCoverAndKeepsTheBlocksWide()
    {
        var backdrop = AlbumBackdrop.Make(Image(Theme.Rgb(255, 0, 0), Theme.Rgb(0, 0, 255)), GridBackdrop.Pixellated, 60, 12)!;
        Assert.NotEqual(backdrop(2, 3), backdrop(57, 3));
        Assert.Equal(backdrop(0, 0), backdrop(1, 0)); // neighbouring cells share a block
    }

    [Fact]
    public void LayoutDefaultsToTheList()
    {
        var ui = new UiSettings();
        Assert.Equal(AlbumLayout.List, ui.AlbumLayout);
        Assert.Equal(GridBackdrop.Pixellated, ui.GridBackground);
    }
}
