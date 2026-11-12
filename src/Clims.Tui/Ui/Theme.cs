using Clims.Tui.Settings;
using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>
/// The colours in use. The values come from the active <see cref="ThemePalette"/> (the chosen theme plus the user's overrides),
/// set with <see cref="Apply(ThemeSettings)"/>; the views read them every time they paint, so a change shows at once.
/// </summary>
public static class Theme
{
    private static ThemePalette _palette = Themes.Default.Palette;

    public static Color Rgb(int r, int g, int b) => new(r, g, b, 255);

    /// <summary>The colour <paramref name="amount"/> (0..1) of the way from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static Color Mix(Color a, Color b, double amount) =>
        Rgb((int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));

    /// <summary>Makes the theme of the settings (with its overrides) the one in use.</summary>
    public static void Apply(ThemeSettings settings) => _palette = Themes.Resolve(settings);

    public static Color Bg => _palette[ThemeSlot.Bg];
    public static Color BgHeader => _palette[ThemeSlot.BgHeader];
    public static Color BgAlt => _palette[ThemeSlot.BgAlt];
    public static Color BgSelected => _palette[ThemeSlot.BgSelected];
    public static Color BgSelectedDim => _palette[ThemeSlot.BgSelectedDim];
    public static Color Fg => _palette[ThemeSlot.Fg];
    public static Color FgDim => _palette[ThemeSlot.FgDim];
    public static Color Accent => _palette[ThemeSlot.Accent];
    public static Color Good => _palette[ThemeSlot.Good];
    public static Color Bad => _palette[ThemeSlot.Bad];
    public static Color Warn => _palette[ThemeSlot.Warn];

    /// <summary>Shown under the pointer on clickable things (the background colour on it, or light text for links).</summary>
    public static Color HoverBg => _palette[ThemeSlot.HoverBg];

    /// <summary>Text colour of a link under the pointer.</summary>
    public static Color HoverFg => _palette[ThemeSlot.HoverFg];

    /// <summary>Frames and scrollbar tracks.</summary>
    public static Color Border => _palette[ThemeSlot.Border];

    /// <summary>Placeholders and unplayed markers.</summary>
    public static Color Faint => _palette[ThemeSlot.Faint];

    /// <summary>Section markers of the album view.</summary>
    public static Color Marker => _palette[ThemeSlot.Marker];

    /// <summary>The thumb of scrollbars.</summary>
    public static Color Thumb => _palette[ThemeSlot.Thumb];

    /// <summary>A button or tab under the pointer.</summary>
    public static Attribute Hover => new(Bg, HoverBg);

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
