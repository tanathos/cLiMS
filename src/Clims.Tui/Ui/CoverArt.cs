using Clims.Tui.Settings;
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

    private static readonly CoverMode[] Styles = [CoverMode.Sixel, CoverMode.Sixel, CoverMode.Sixel];

    /// <summary>The style that stands in for Sixel where it cannot be used (no terminal support, or a cover partly out of view).</summary>
    public static CoverMode SixelFallback { get; set; } = CoverMode.Quadrant;

    /// <summary>The size of the covers in the library and recently added lists.</summary>
    public static CoverSize LibrarySize { get; set; } = CoverSize.Medium;

    /// <summary>The chosen style of a place: how many image samples one terminal cell carries, or a real image (Sixel).</summary>
    public static CoverMode Style(CoverPlace place) => Styles[(int)place];

    public static void SetStyle(CoverPlace place, CoverMode style) => Styles[(int)place] = style;

    /// <summary>Takes the styles and the library size from the settings.</summary>
    public static void Apply(CoverStylesSettings settings)
    {
        SixelFallback = settings.SixelFallback;
        SetStyle(CoverPlace.Footer, settings.Footer.Style);
        SetStyle(CoverPlace.Library, settings.Library.Style);
        SetStyle(CoverPlace.FullScreen, settings.FullScreen.Style);
        LibrarySize = settings.Library.Size ?? CoverSize.Medium;
    }

    /// <summary>True when the place wants Sixel and the terminal supports it.</summary>
    public static bool UsesSixel(CoverPlace place) => Style(place) == CoverMode.Sixel && SixelCovers.Available;

    /// <summary>The cell style to draw with: the place's own, or the fallback where Sixel is chosen. Never Sixel.</summary>
    public static CoverMode CellMode(CoverPlace place)
    {
        var style = Style(place);
        if (style == CoverMode.Sixel)
        {
            style = SixelFallback;
        }

        return style == CoverMode.Sixel ? CoverMode.Quadrant : style;
    }

    /// <summary>The next style for a place, skipping Sixel on terminals without it.</summary>
    public static CoverMode NextStyle(CoverPlace place)
    {
        var style = Style(place);
        do
        {
            style = (CoverMode)(((int)style + 1) % 4);
        }
        while (style == CoverMode.Sixel && !SixelCovers.Available);

        return style;
    }

    /// <summary>The next library size, after the largest the smallest again.</summary>
    public static CoverSize NextSize(CoverSize size) => (CoverSize)(((int)size + 1) % 3);

    /// <summary>The cells one library cover takes: 16x8, 24x12 (the default) or 32x16.</summary>
    public static (int Cols, int Rows) LibraryCells(CoverSize size) => size switch
    {
        CoverSize.Small => (16, 8),
        CoverSize.Large => (32, 16),
        _ => (24, 12),
    };

    /// <summary>Glyph plus colours for one cell of <paramref name="image"/> drawn in <paramref name="cellMode"/> (see <see cref="CellMode"/>).</summary>
    public static (string Glyph, Color Fg, Color Bg) Glyph(CoverImage image, int col, int row, int cols, int rows, CoverMode cellMode)
    {
        var mode = cellMode == CoverMode.Sixel ? CoverMode.Quadrant : cellMode;
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
