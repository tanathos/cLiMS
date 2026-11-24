using Clims.Tui.Ui;
using Terminal.Gui.Drawing;

namespace Clims.Tui.Tests;

public class CoverImageTests
{
    private static CoverImage Colourful() => CoverImage.FromPixels(
        new Color[,]
        {
            { Theme.Rgb(255, 0, 0), Theme.Rgb(0, 255, 0) },
            { Theme.Rgb(0, 0, 255), Theme.Rgb(255, 255, 255) },
        },
        "cover");

    [Fact]
    public void Grayscale_has_equal_channels_and_the_luma_of_each_pixel()
    {
        using var image = Colourful();

        var gray = image.Grayscale();

        var red = gray.Pixel(2, 2, 0, 0);
        var green = gray.Pixel(2, 2, 0, 1); // the array is indexed [x, y]
        var blue = gray.Pixel(2, 2, 1, 0);
        var white = gray.Pixel(2, 2, 1, 1);
        foreach (var c in new[] { red, green, blue, white })
        {
            Assert.Equal(c.R, c.G);
            Assert.Equal(c.G, c.B);
        }

        Assert.InRange(red.R, 50, 58);     // 0.2126 * 255
        Assert.InRange(green.R, 178, 186); // 0.7152 * 255
        Assert.InRange(blue.R, 15, 21);    // 0.0722 * 255
        Assert.InRange(white.R, 252, 255);
    }

    [Fact]
    public void Grayscale_is_made_once_and_has_its_own_label()
    {
        using var image = Colourful();

        Assert.Same(image.Grayscale(), image.Grayscale());
        Assert.NotEqual(image.Label, image.Grayscale().Label);
        Assert.Equal((image.PixelWidth, image.PixelHeight), (image.Grayscale().PixelWidth, image.Grayscale().PixelHeight));
    }
}
