using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>The fixed palettes of the retro cover styles, and the ordered (Bayer) dithering that maps a pixel onto one.</summary>
public static class CoverPalette
{
    private static readonly Color[] GameBoyColors = Parse("0f380f", "306230", "8bac0f", "9bbc0f");

    private static readonly Color[] C64Colors = Parse(
        "000000", "ffffff", "880000", "aaffee", "cc44cc", "00cc55", "0000aa", "eeee77",
        "dd8855", "664400", "ff7777", "333333", "777777", "aaff66", "0088ff", "bbbbbb");

    private static readonly int[,] Bayer = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };

    /// <summary>True for the styles that draw with a palette.</summary>
    public static bool IsPalette(CoverMode style) => style is CoverMode.GameBoy or CoverMode.C64 or CoverMode.GameBoyQuadrant or CoverMode.C64Quadrant;

    /// <summary>True for the palette styles drawn with octants (2 x 4 samples per cell) rather than quadrants (2 x 2).</summary>
    public static bool UsesOctants(CoverMode style) => style is CoverMode.GameBoy or CoverMode.C64;

    private static bool IsGameBoy(CoverMode style) => style is CoverMode.GameBoy or CoverMode.GameBoyQuadrant;

    /// <summary>The colours of the palette of <paramref name="style"/>.</summary>
    public static IReadOnlyList<Color> Colors(CoverMode style) => IsGameBoy(style) ? GameBoyColors : C64Colors;

    /// <summary>The colour of the palette of <paramref name="style"/> that stands for <paramref name="c"/> at pixel (x, y).</summary>
    public static Color Map(CoverMode style, Color c, int x, int y) => Colors(style)[Index(style, c, x, y)];

    /// <summary>The index in <see cref="Colors"/> of the colour that stands for <paramref name="c"/> at pixel (x, y), after the Bayer offset.</summary>
    public static int Index(CoverMode style, Color c, int x, int y)
    {
        var palette = Colors(style);
        var strength = IsGameBoy(style) ? 64 : 40;
        var offset = (Bayer[y & 3, x & 3] / 15.0 - 0.5) * strength;
        double r = c.R + offset, g = c.G + offset, b = c.B + offset;
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < palette.Count; i++)
        {
            double dr = r - palette[i].R, dg = g - palette[i].G, db = b - palette[i].B;
            var d = 3 * dr * dr + 4 * dg * dg + 2 * db * db;
            if (d < bestDistance)
            {
                (best, bestDistance) = (i, d);
            }
        }

        return best;
    }

    /// <summary>How far apart two colours look (the weights of the nearest-colour search).</summary>
    public static double Distance(Color a, Color b)
    {
        double dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return 3 * dr * dr + 4 * dg * dg + 2 * db * db;
    }

    private static Color[] Parse(params string[] hex) =>
        hex.Select(h => Theme.Rgb(Convert.ToInt32(h[..2], 16), Convert.ToInt32(h[2..4], 16), Convert.ToInt32(h[4..], 16))).ToArray();
}
