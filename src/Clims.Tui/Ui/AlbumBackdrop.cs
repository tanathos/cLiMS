using Clims.Tui.Settings;
using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>The background colours of the tracklist panel of the grid layout, derived from the cover and held back towards the theme background.</summary>
public static class AlbumBackdrop
{
    /// <summary>How much of the cover's colour shows through (0..1). Low enough that normal text stays readable on any cover.</summary>
    public const double Strength = 0.30;

    /// <summary>The mean colour of the cover.</summary>
    public static Color Average(CoverImage image)
    {
        int r = 0, g = 0, b = 0;
        const int n = 4;
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                var c = image.Pixel(n, n, x, y);
                r += c.R;
                g += c.G;
                b += c.B;
            }
        }

        return Theme.Rgb(r / (n * n), g / (n * n), b / (n * n));
    }

    /// <summary>The colour to draw where the cover has <paramref name="pixel"/>: the theme background with a share of it.</summary>
    public static Color Soften(Color pixel) => Theme.Mix(Theme.Bg, pixel, Strength);

    /// <summary>
    /// The colour of cell (x, y) of a panel <paramref name="cols"/> x <paramref name="rows"/> cells large, or null when the
    /// cover is not loaded yet (the panel then has the theme background). Pixellated blocks are about 6 cells wide and 3 high.
    /// </summary>
    public static Func<int, int, Color>? Make(CoverImage? image, GridBackdrop kind, int cols, int rows)
    {
        if (image is null || cols <= 0 || rows <= 0)
        {
            return null;
        }

        if (kind == GridBackdrop.Tint)
        {
            var flat = Soften(Average(image));
            return (_, _) => flat;
        }

        var gw = Math.Max(4, cols / 6);
        var gh = Math.Max(2, rows / 3);
        return (x, y) => Soften(image.Pixel(gw, gh, Math.Clamp(x * gw / cols, 0, gw - 1), Math.Clamp(y * gh / rows, 0, gh - 1)));
    }
}
