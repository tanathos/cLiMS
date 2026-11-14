using System.Text.RegularExpressions;
using Clims.Tui.Ui;
using Terminal.Gui.Drawing;

namespace Clims.Tui.Tests;

public class SixelPictureTests
{
    private static Color[,] Picture(int w, int h)
    {
        var pixels = new Color[w, h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                pixels[x, y] = new Color(x * 255 / w, y * 255 / h, (x * 7 + y * 3) % 256);
            }
        }

        return pixels;
    }

    [Fact]
    public void A_picture_with_many_colours_keeps_a_palette_of_many_colours()
    {
        var data = SixelPicture.Encode(Picture(120, 90));

        Assert.StartsWith("\u001bP0;0;0q\"1;1;120;90", data);
        Assert.EndsWith("\u001b\\", data);
        var colours = Regex.Matches(data, @"#\d+;2;\d+;\d+;\d+").Count;
        Assert.InRange(colours, 200, 256);
    }

    [Fact]
    public void A_picture_with_few_colours_gets_exactly_those()
    {
        var pixels = new Color[20, 12];
        for (var y = 0; y < 12; y++)
        {
            for (var x = 0; x < 20; x++)
            {
                pixels[x, y] = x < 10 ? new Color(255, 0, 0) : new Color(0, 0, 255);
            }
        }

        var data = SixelPicture.Encode(pixels);

        Assert.Contains(";2;100;0;0", data);
        Assert.Contains(";2;0;0;100", data);
        Assert.Equal(2, Regex.Matches(data, @"#\d+;2;").Count);
    }

    [Fact]
    public void Avoiding_the_bottom_scroll_drops_one_band_of_six_rows()
    {
        var data = SixelPicture.Encode(Picture(30, 60), avoidBottomScroll: true);

        Assert.StartsWith("\u001bP0;0;0q\"1;1;30;54", data);
    }
}
