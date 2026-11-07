using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>Chooses how covers are drawn and turns a <see cref="CoverImage"/> into terminal cells.</summary>
public static class CoverArt
{
    private static readonly char[] Quadrants =
        { ' ', '▘', '▝', '▀', '▖', '▌', '▞', '▛', '▗', '▚', '▐', '▜', '▄', '▙', '▟', '█' };

    // Braille dot bit for (column 0..1, row 0..3).
    private static readonly int[,] BrailleBits = { { 0x01, 0x02, 0x04, 0x40 }, { 0x08, 0x10, 0x20, 0x80 } };

    /// <summary>Where cover images come from. Set once at startup (generated covers for the demo, the server otherwise).</summary>
    public static ICoverSource Source { get; set; } = new ProceduralCoverSource();

    /// <summary>How many image samples one terminal cell carries (and so how sharp a cover of a given cell size is).</summary>
    public static CoverMode Mode { get; set; } = CoverMode.Sixel;

    /// <summary>True when Sixel is both selected and supported by the terminal.</summary>
    public static bool UseSixel => Mode == CoverMode.Sixel && SixelCovers.Available;

    /// <summary>Bigger list covers (24x12 cells instead of 16x8).</summary>
    public static bool Large { get; set; } = true;

    /// <summary>Glyph plus colours for one cell of <paramref name="image"/> in the current <see cref="Mode"/>.</summary>
    public static (string Glyph, Color Fg, Color Bg) Glyph(CoverImage image, int col, int row, int cols, int rows)
    {
        var mode = Mode == CoverMode.Sixel ? CoverMode.Quadrant : Mode;
        if (mode == CoverMode.HalfBlock)
        {
            return ("▀", image.Pixel(cols, rows * 2, col, row * 2), image.Pixel(cols, rows * 2, col, row * 2 + 1));
        }

        // Sample a 2 x subY grid inside the cell, split it into a bright and a dark group (2-means on luminance),
        // and let the glyph pattern say which sub-pixels use the foreground colour.
        var subY = mode == CoverMode.Quadrant ? 2 : 4;
        var samples = new Color[2 * subY];
        var lum = new double[samples.Length];
        for (var sy = 0; sy < subY; sy++)
        {
            for (var sx = 0; sx < 2; sx++)
            {
                var c = image.Pixel(cols * 2, rows * subY, col * 2 + sx, row * subY + sy);
                samples[sy * 2 + sx] = c;
                lum[sy * 2 + sx] = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            }
        }

        var mean = lum.Average();
        var set = new List<int>();
        var unset = new List<int>();
        for (var i = 0; i < samples.Length; i++)
        {
            (lum[i] > mean ? set : unset).Add(i);
        }

        if (set.Count == 0 || unset.Count == 0 || lum.Max() - lum.Min() < 6)
        {
            var avg = Average(samples);
            return (" ", avg, avg);
        }

        var fg = Average(set.Select(i => samples[i]));
        var bg = Average(unset.Select(i => samples[i]));
        var mask = 0;
        foreach (var i in set)
        {
            int sx = i % 2, sy = i / 2;
            mask |= mode == CoverMode.Quadrant ? 1 << (sx + 2 * sy) : BrailleBits[sx, sy];
        }

        return (mode == CoverMode.Quadrant ? Quadrants[mask].ToString() : ((char)(0x2800 + mask)).ToString(), fg, bg);
    }

    private static Color Average(IEnumerable<Color> colors)
    {
        int r = 0, g = 0, b = 0, n = 0;
        foreach (var c in colors)
        {
            r += c.R;
            g += c.G;
            b += c.B;
            n++;
        }

        return Theme.Rgb(r / n, g / n, b / n);
    }
}
