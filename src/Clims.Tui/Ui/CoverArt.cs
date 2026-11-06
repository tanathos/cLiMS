using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

public enum CoverMode
{
    /// <summary>"▀": 1 x 2 samples per cell, two colours.</summary>
    HalfBlock,

    /// <summary>Quadrant blocks: 2 x 2 samples per cell, two colours.</summary>
    Quadrant,

    /// <summary>Braille dots: 2 x 4 samples per cell, two colours.</summary>
    Braille,

    /// <summary>Real pixels via Sixel when the terminal supports it (partly visible covers fall back to quadrants).</summary>
    Sixel,
}

/// <summary>
/// Fake covers drawn as true-colour half-block cells: one cell holds two vertically stacked pixels
/// (foreground = upper half "▀", background = lower half). Real covers (Step 3) go through the same cell grid.
/// </summary>
public static class CoverArt
{
    private sealed record Recipe(double Hue, double Hue2, double Cx, double Cy, double Rad, int Stripe);

    private static readonly Dictionary<int, Recipe> Recipes = new();

    private static Recipe RecipeFor(int seed)
    {
        if (!Recipes.TryGetValue(seed, out var recipe))
        {
            var r = new Random(seed);
            var hue = r.NextDouble() * 360;
            recipe = new Recipe(
                hue,
                (hue + 40 + r.NextDouble() * 120) % 360,
                0.3 + r.NextDouble() * 0.4,
                0.3 + r.NextDouble() * 0.4,
                0.15 + r.NextDouble() * 0.2,
                r.Next(3));
            Recipes[seed] = recipe;
        }

        return recipe;
    }

    public static Color Pixel(int seed, double u, double v)
    {
        var p = RecipeFor(seed);
        var d = Math.Sqrt((u - p.Cx) * (u - p.Cx) + (v - p.Cy) * (v - p.Cy));
        var t = p.Stripe == 0 ? v : p.Stripe == 1 ? u : (u + v) / 2;
        var inside = d < p.Rad;
        return FromHsl(inside ? p.Hue2 : p.Hue, 0.55, inside ? 0.62 : 0.18 + 0.32 * t);
    }

    /// <summary>
    /// Real covers loaded by <c>DevCovers</c>. When present, every album maps to one of them by seed so they show
    /// up everywhere (lists, footer). Empty means the procedural fakes below are used.
    /// </summary>
    public static IReadOnlyList<CoverImage> Real { get; set; } = Array.Empty<CoverImage>();

    /// <summary>How many image samples one terminal cell carries (and so how sharp a cover of a given cell size is).</summary>
    public static CoverMode Mode { get; set; } = CoverMode.Sixel;

    public static CoverImage? ImageFor(int seed) => Real.Count == 0 ? null : Real[(seed & int.MaxValue) % Real.Count];

    /// <summary>The mode actually used: Sixel needs a supporting terminal and real covers, else quadrants.</summary>
    public static bool UseSixel => Mode == CoverMode.Sixel && Real.Count > 0 && SixelCovers.Available;

    /// <summary>Bigger list covers (24x12 cells instead of 16x8).</summary>
    public static bool Large { get; set; } = true;

    private static readonly char[] Quadrants =
        { ' ', '▘', '▝', '▀', '▖', '▌', '▞', '▛', '▗', '▚', '▐', '▜', '▄', '▙', '▟', '█' };

    // Braille dot bit for (column 0..1, row 0..3).
    private static readonly int[,] BrailleBits = { { 0x01, 0x02, 0x04, 0x40 }, { 0x08, 0x10, 0x20, 0x80 } };

    /// <summary>Glyph plus colours for one cell in the current <see cref="Mode"/>.</summary>
    public static (string Glyph, Color Fg, Color Bg) Glyph(int seed, int col, int row, int cols, int rows)
    {
        var mode = Mode == CoverMode.Sixel ? CoverMode.Quadrant : Mode;
        if (Real.Count == 0 || mode == CoverMode.HalfBlock)
        {
            var (top, bottom) = Cell(seed, col, row, cols, rows);
            return ("▀", top, bottom);
        }

        // Sample a 2 x subY grid inside the cell, split it into a bright and a dark group (2-means on luminance),
        // and let the glyph pattern say which sub-pixels use the foreground colour.
        var subY = mode == CoverMode.Quadrant ? 2 : 4;
        var image = Real[(seed & int.MaxValue) % Real.Count];
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
            var avg = Average(Enumerable.Range(0, samples.Length).Select(i => samples[i]));
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

    /// <summary>Returns the foreground (top pixel) and background (bottom pixel) of a cell.</summary>
    public static (Color Top, Color Bottom) Cell(int seed, int col, int row, int cols, int rows)
    {
        if (Real.Count > 0)
        {
            var image = Real[(seed & int.MaxValue) % Real.Count];
            return (image.Pixel(cols, rows * 2, col, row * 2), image.Pixel(cols, rows * 2, col, row * 2 + 1));
        }

        var u = (col + 0.5) / cols;
        var vTop = (row * 2 + 0.5) / (rows * 2);
        var vBottom = (row * 2 + 1.5) / (rows * 2);
        return (Pixel(seed, u, vTop), Pixel(seed, u, vBottom));
    }

    private static Color FromHsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Theme.Rgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }
}
