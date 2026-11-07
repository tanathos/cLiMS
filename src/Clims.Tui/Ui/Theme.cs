using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>Dark true-colour palette, loosely based on the MusicBee reference screenshot.</summary>
public static class Theme
{
    public static Color Rgb(int r, int g, int b) => new(r, g, b, 255);

    /// <summary>The colour <paramref name="amount"/> (0..1) of the way from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static Color Mix(Color a, Color b, double amount) =>
        Rgb((int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));

    public static readonly Color Bg = Rgb(24, 26, 31);
    public static readonly Color BgHeader = Rgb(16, 18, 23);
    public static readonly Color BgAlt = Rgb(40, 44, 54);
    public static readonly Color BgSelected = Rgb(52, 84, 130);
    public static readonly Color BgSelectedDim = Rgb(42, 48, 60);
    public static readonly Color Fg = Rgb(214, 218, 226);
    public static readonly Color FgDim = Rgb(128, 134, 148);
    public static readonly Color Accent = Rgb(110, 190, 255);
    public static readonly Color Good = Rgb(112, 200, 130);
    public static readonly Color Bad = Rgb(228, 104, 104);
    public static readonly Color Warn = Rgb(240, 190, 90);

    /// <summary>Scheme for stock views (the search TextField); owner-drawn views paint with the attributes below.</summary>
    public static Scheme Dark => new(Normal)
    {
        Editable = new Attribute(Fg, BgAlt),
        Focus = new Attribute(Fg, BgSelected),
        HotNormal = new Attribute(Accent, Bg),
        HotFocus = new Attribute(Accent, BgSelected),
    };

    public static Attribute Normal => new(Fg, Bg);

    public static Attribute Alt => new(Fg, BgAlt);

    public static Attribute Dim => new(FgDim, Bg);

    public static Attribute Heading => new(Accent, Bg);

    public static Attribute Selected => new(Fg, BgSelected);

    public static Attribute SelectedDim => new(Fg, BgSelectedDim);
}
