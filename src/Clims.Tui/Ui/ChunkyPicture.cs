using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>A cover reduced to a small grid of square blocks (pixel art) and enlarged again, for the chunky Sixel styles.</summary>
public static class ChunkyPicture
{
    /// <summary>Pixels along the short side of the picture: 48 for <see cref="CoverMode.Chunky"/>, 64 with scanlines.</summary>
    public static int GridOf(CoverMode style) => style == CoverMode.ChunkyScanlines ? 64 : 48;

    /// <summary>True for the styles drawn with <see cref="Make"/>.</summary>
    public static bool IsChunky(CoverMode style) => style is CoverMode.Chunky or CoverMode.ChunkyScanlines;

    /// <summary>The cover as <paramref name="width"/> x <paramref name="height"/> pixels made of square blocks.</summary>
    public static Color[,] Make(CoverImage image, int width, int height, CoverMode style)
    {
        var block = Math.Max(1, (int)Math.Round(Math.Min(width, height) / (double)GridOf(style)));
        var gw = Math.Max(1, (width + block - 1) / block);
        var gh = Math.Max(1, (height + block - 1) / block);
        var small = image.Bitmap(gw, gh);
        var scanlines = style == CoverMode.ChunkyScanlines && block >= 2;
        var dark = block - Math.Max(1, block * 2 / 5);
        var result = new Color[width, height];
        for (var y = 0; y < height; y++)
        {
            var line = scanlines && y % block >= dark;
            for (var x = 0; x < width; x++)
            {
                var c = small[Math.Min(x / block, gw - 1), Math.Min(y / block, gh - 1)];
                result[x, y] = line ? Theme.Rgb(c.R * 65 / 100, c.G * 65 / 100, c.B * 65 / 100) : c;
            }
        }

        return result;
    }
}
