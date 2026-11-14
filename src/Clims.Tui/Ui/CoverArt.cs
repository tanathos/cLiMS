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

    /// <summary>The size of the full-screen cover (this session only): <see cref="CoverSize.Large"/> fills the panel, the others are 75% and 50% of that.</summary>
    public static CoverSize FullScreenSize { get; set; } = CoverSize.Large;

    /// <summary>The share of the full-screen panel the cover takes, in percent.</summary>
    public static int FullScreenPercent(CoverSize size) => size switch
    {
        CoverSize.Small => 50,
        CoverSize.Medium => 75,
        _ => 100,
    };

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

    /// <summary>How the Library and Recently added tabs list albums (F7 changes it for the session).</summary>
    public static AlbumLayout Layout { get; set; } = AlbumLayout.List;

    /// <summary>The background of the grid layout's tracklist panel.</summary>
    public static GridBackdrop Backdrop { get; set; } = GridBackdrop.Pixellated;

    /// <summary>Takes the album layout and the panel background from the settings.</summary>
    public static void ApplyLayout(UiSettings ui)
    {
        Layout = ui.AlbumLayout;
        Backdrop = ui.GridBackground;
    }

    /// <summary>The styles in the order F3 goes through them.</summary>
    public static readonly IReadOnlyList<CoverMode> Order =
    [
        CoverMode.Sixel, CoverMode.HalfBlock, CoverMode.Quadrant, CoverMode.Braille, CoverMode.Sextant, CoverMode.Octant,
        CoverMode.GameBoy, CoverMode.C64, CoverMode.GameBoyQuadrant, CoverMode.C64Quadrant, CoverMode.Chunky, CoverMode.ChunkyScanlines,
    ];

    /// <summary>What a style needs from the terminal or the font, or null when it works everywhere.</summary>
    public static string? Warning(CoverMode style) => style switch
    {
        CoverMode.Sixel => "needs a terminal with Sixel",
        CoverMode.Chunky or CoverMode.ChunkyScanlines => "needs a terminal with Sixel (else the fallback)",
        CoverMode.Sextant => "needs a font with sextant characters (Unicode 13)",
        CoverMode.Octant or CoverMode.GameBoy or CoverMode.C64 => "needs a font with octant characters (Unicode 16), still rare",
        _ => null,
    };

    /// <summary>True for the styles that need a Sixel image (the real one, or a chunky one).</summary>
    public static bool IsSixelStyle(CoverMode style) => style == CoverMode.Sixel || ChunkyPicture.IsChunky(style);

    /// <summary>True when the place wants a Sixel image and the terminal supports it.</summary>
    public static bool UsesSixel(CoverPlace place) => IsSixelStyle(Style(place)) && SixelCovers.Available;

    /// <summary>The cell style to draw with: the place's own, or the fallback where a Sixel style is chosen. Never a Sixel style.</summary>
    public static CoverMode CellMode(CoverPlace place)
    {
        var style = Style(place);
        if (IsSixelStyle(style))
        {
            style = SixelFallback;
        }

        return IsSixelStyle(style) ? CoverMode.Quadrant : style;
    }

    /// <summary>The next style for a place, skipping the Sixel styles on terminals without Sixel.</summary>
    public static CoverMode NextStyle(CoverPlace place)
    {
        var index = Math.Max(0, Order.ToList().IndexOf(Style(place)));
        CoverMode style;
        do
        {
            index = (index + 1) % Order.Count;
            style = Order[index];
        }
        while (IsSixelStyle(style) && !SixelCovers.Available);

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
        var mode = IsSixelStyle(cellMode) ? CoverMode.Quadrant : cellMode;
        if (mode == CoverMode.HalfBlock)
        {
            return ("▀", image.Pixel(cols, rows * 2, col, row * 2), image.Pixel(cols, rows * 2, col, row * 2 + 1));
        }

        if (CoverPalette.IsPalette(mode))
        {
            return PaletteCell(image, col, row, cols, rows, mode);
        }

        // Sample a 2 x subY grid inside the cell, split it into a bright and a dark group (2-means on luminance),
        // and let the glyph pattern say which sub-pixels use the foreground colour.
        var subY = mode switch { CoverMode.Quadrant => 2, CoverMode.Sextant => 3, _ => 4 };
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
            mask |= mode == CoverMode.Braille ? BrailleBits[sx, sy] : 1 << i;   // the others: one bit per sample, row by row
        }

        var glyph = mode switch
        {
            CoverMode.Quadrant => Quadrants[mask].ToString(),
            CoverMode.Sextant => SextantGlyph(mask),
            CoverMode.Octant => OctantGlyph(mask),
            _ => ((char)(0x2800 + mask)).ToString(),
        };
        return (glyph, fg, bg);
    }

    /// <summary>
    /// A cell of the palette styles: 2 x 4 samples, each dithered onto the palette. A cell holds two colours, so the two
    /// palette colours that occur most stand for all eight samples, drawn with an octant block.
    /// </summary>
    private static (string Glyph, Color Fg, Color Bg) PaletteCell(CoverImage image, int col, int row, int cols, int rows, CoverMode mode)
    {
        var palette = CoverPalette.Colors(mode);
        var subY = CoverPalette.UsesOctants(mode) ? 4 : 2;
        var index = new int[2 * subY];
        for (var i = 0; i < index.Length; i++)
        {
            int x = col * 2 + i % 2, y = row * subY + i / 2;
            index[i] = CoverPalette.Index(mode, image.Pixel(cols * 2, rows * subY, x, y), x, y);
        }

        var ranked = index.GroupBy(i => i).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).ToList();
        var first = ranked[0];
        if (ranked.Count == 1)
        {
            return (" ", palette[first], palette[first]);
        }

        var second = ranked[1];
        var mask = 0;
        for (var i = 0; i < index.Length; i++)
        {
            var toFirst = index[i] == first
                || (index[i] != second && CoverPalette.Distance(palette[index[i]], palette[first]) <= CoverPalette.Distance(palette[index[i]], palette[second]));
            if (toFirst)
            {
                mask |= 1 << i;
            }
        }

        return (subY == 4 ? OctantGlyph(mask) : Quadrants[mask].ToString(), palette[first], palette[second]);
    }

    /// <summary>The block character for a 2 x 3 pattern (bit 0 top left, bit 1 top right, then row by row).</summary>
    public static string SextantGlyph(int mask) => mask switch
    {
        0 => " ",
        21 => "▌",
        42 => "▐",
        63 => "█",
        _ => char.ConvertFromUtf32(0x1FB00 + mask - 1 - (mask > 21 ? 1 : 0) - (mask > 42 ? 1 : 0)),
    };

    // The patterns of a 2 x 4 grid that Unicode never gave an octant character: older block characters stand for them.
    private static readonly Dictionary<int, string> OctantOlder = new()
    {
        [0] = " ", [1] = "\U0001CEA8", [2] = "\U0001CEAB", [3] = "\U0001FB82", [5] = "\U0001CEA7", [10] = "\U0001CEAC",
        [15] = "▀", [20] = "\U0001FBE6", [40] = "\U0001FBE7", [63] = "\U0001FB85", [64] = "\U0001CEA3", [80] = "\U0001CEA4",
        [85] = "▌", [90] = "▞", [95] = "▛", [128] = "\U0001CEA0", [160] = "\U0001CEAF", [165] = "▚", [170] = "▐",
        [175] = "▜", [192] = "▂", [240] = "▄", [245] = "▙", [250] = "▟", [252] = "▆", [255] = "█",
    };

    private static readonly string[] Octants = BuildOctants();

    /// <summary>The block character for a 2 x 4 pattern (bit 0 top left, bit 1 top right, then row by row).</summary>
    public static string OctantGlyph(int mask) => Octants[mask & 255];

    private static string[] BuildOctants()
    {
        // The 230 octant characters (U+1CD00...) are in the order of their patterns, leaving out those with an older character.
        var all = new string[256];
        var next = 0x1CD00;
        for (var m = 0; m < 256; m++)
        {
            all[m] = OctantOlder.TryGetValue(m, out var older) ? older : char.ConvertFromUtf32(next++);
        }

        return all;
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
