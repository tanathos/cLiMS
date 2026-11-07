using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Clims.Lms;
using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>Generates fake covers (gradient plus a blob) from a seed.</summary>
public static class ProceduralCovers
{
    private sealed record Recipe(double Hue, double Hue2, double Cx, double Cy, double Rad, int Stripe);

    /// <summary>A 96x96 generated cover.</summary>
    public static CoverImage Create(int seed)
    {
        const int size = 96;
        var recipe = RecipeFor(seed);
        var pixels = new Color[size, size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                pixels[x, y] = Pixel(recipe, (x + 0.5) / size, (y + 0.5) / size);
            }
        }

        return CoverImage.FromPixels(pixels, "demo" + seed);
    }

    private static Recipe RecipeFor(int seed)
    {
        var r = new Random(seed);
        var hue = r.NextDouble() * 360;
        return new Recipe(hue, (hue + 40 + r.NextDouble() * 120) % 360, 0.3 + r.NextDouble() * 0.4, 0.3 + r.NextDouble() * 0.4, 0.15 + r.NextDouble() * 0.2, r.Next(3));
    }

    private static Color Pixel(Recipe p, double u, double v)
    {
        var d = Math.Sqrt((u - p.Cx) * (u - p.Cx) + (v - p.Cy) * (v - p.Cy));
        var t = p.Stripe == 0 ? v : p.Stripe == 1 ? u : (u + v) / 2;
        var inside = d < p.Rad;
        return FromHsl(inside ? p.Hue2 : p.Hue, 0.55, inside ? 0.62 : 0.18 + 0.32 * t);
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
