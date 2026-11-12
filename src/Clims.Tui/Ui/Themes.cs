using Clims.Tui.Settings;
using Terminal.Gui.Drawing;
using static Clims.Tui.Ui.ThemeSlot;

namespace Clims.Tui.Ui;

/// <summary>The predefined themes. To add one, add an entry to <see cref="All"/>.</summary>
public static class Themes
{
    private static Color C(int r, int g, int b) => Theme.Rgb(r, g, b);

    /// <summary>The dark theme cLiMS always had, loosely based on the MusicBee reference screenshot.</summary>
    public static ThemeDefinition Default { get; } = new("Default", new ThemePalette(new Dictionary<ThemeSlot, Color>
    {
        [Bg] = C(24, 26, 31), [BgHeader] = C(16, 18, 23), [BgAlt] = C(40, 44, 54), [BgSelected] = C(52, 84, 130), [BgSelectedDim] = C(42, 48, 60),
        [Fg] = C(214, 218, 226), [FgDim] = C(128, 134, 148), [Accent] = C(110, 190, 255), [Good] = C(112, 200, 130), [Bad] = C(228, 104, 104),
        [Warn] = C(240, 190, 90), [HoverBg] = C(160, 166, 180), [HoverFg] = C(250, 251, 253),
        [Border] = C(52, 57, 70), [Faint] = C(70, 76, 92), [Marker] = C(95, 102, 122), [Thumb] = C(150, 158, 176),
    }));

    public static ThemeDefinition Light { get; } = new("Light", new ThemePalette(new Dictionary<ThemeSlot, Color>
    {
        [Bg] = C(247, 247, 249), [BgHeader] = C(230, 232, 238), [BgAlt] = C(233, 235, 240), [BgSelected] = C(176, 208, 245), [BgSelectedDim] = C(216, 222, 232),
        [Fg] = C(36, 40, 48), [FgDim] = C(110, 116, 130), [Accent] = C(20, 100, 190), [Good] = C(30, 140, 60), [Bad] = C(200, 50, 50),
        [Warn] = C(180, 110, 0), [HoverBg] = C(90, 96, 110), [HoverFg] = C(255, 255, 255),
        [Border] = C(205, 209, 218), [Faint] = C(180, 185, 196), [Marker] = C(140, 146, 160), [Thumb] = C(130, 136, 150),
    }));

    public static ThemeDefinition Nord { get; } = new("Nord", new ThemePalette(new Dictionary<ThemeSlot, Color>
    {
        [Bg] = C(46, 52, 64), [BgHeader] = C(36, 41, 51), [BgAlt] = C(59, 66, 82), [BgSelected] = C(76, 86, 106), [BgSelectedDim] = C(67, 76, 94),
        [Fg] = C(216, 222, 233), [FgDim] = C(129, 142, 163), [Accent] = C(136, 192, 208), [Good] = C(163, 190, 140), [Bad] = C(191, 97, 106),
        [Warn] = C(235, 203, 139), [HoverBg] = C(129, 161, 193), [HoverFg] = C(236, 239, 244),
        [Border] = C(67, 76, 94), [Faint] = C(76, 86, 106), [Marker] = C(97, 110, 136), [Thumb] = C(129, 161, 193),
    }));

    public static ThemeDefinition HighContrast { get; } = new("High contrast", new ThemePalette(new Dictionary<ThemeSlot, Color>
    {
        [Bg] = C(0, 0, 0), [BgHeader] = C(24, 24, 24), [BgAlt] = C(32, 32, 32), [BgSelected] = C(0, 90, 200), [BgSelectedDim] = C(56, 56, 56),
        [Fg] = C(255, 255, 255), [FgDim] = C(190, 190, 190), [Accent] = C(90, 220, 255), [Good] = C(80, 255, 120), [Bad] = C(255, 90, 90),
        [Warn] = C(255, 220, 60), [HoverBg] = C(220, 220, 220), [HoverFg] = C(255, 255, 255),
        [Border] = C(120, 120, 120), [Faint] = C(130, 130, 130), [Marker] = C(170, 170, 170), [Thumb] = C(230, 230, 230),
    }));

    /// <summary>Warm off-white with sepia ink, like a printed page.</summary>
    public static ThemeDefinition Paper { get; } = new("Paper", new ThemePalette(new Dictionary<ThemeSlot, Color>
    {
        [Bg] = C(250, 246, 238), [BgHeader] = C(236, 229, 215), [BgAlt] = C(240, 234, 221), [BgSelected] = C(214, 196, 160), [BgSelectedDim] = C(228, 220, 204),
        [Fg] = C(58, 50, 40), [FgDim] = C(130, 120, 104), [Accent] = C(150, 70, 30), [Good] = C(70, 120, 50), [Bad] = C(170, 40, 40),
        [Warn] = C(170, 110, 10), [HoverBg] = C(100, 88, 72), [HoverFg] = C(255, 252, 245),
        [Border] = C(218, 208, 188), [Faint] = C(196, 186, 166), [Marker] = C(160, 148, 126), [Thumb] = C(150, 138, 116),
    }));

    /// <summary>Light and cheerful: cream and yellow with orange accents.</summary>
    public static ThemeDefinition Sunny { get; } = new("Sunny", new ThemePalette(new Dictionary<ThemeSlot, Color>
    {
        [Bg] = C(255, 251, 235), [BgHeader] = C(255, 238, 170), [BgAlt] = C(255, 244, 204), [BgSelected] = C(255, 214, 102), [BgSelectedDim] = C(252, 232, 160),
        [Fg] = C(50, 44, 20), [FgDim] = C(135, 120, 70), [Accent] = C(190, 80, 0), [Good] = C(40, 130, 50), [Bad] = C(200, 40, 40),
        [Warn] = C(160, 100, 0), [HoverBg] = C(120, 96, 20), [HoverFg] = C(255, 255, 255),
        [Border] = C(240, 220, 150), [Faint] = C(225, 205, 140), [Marker] = C(190, 160, 70), [Thumb] = C(190, 150, 40),
    }));

    /// <summary>Near-black and neutral, with a violet accent.</summary>
    public static ThemeDefinition Obsidian { get; } = new("Obsidian", new ThemePalette(new Dictionary<ThemeSlot, Color>
    {
        [Bg] = C(15, 15, 17), [BgHeader] = C(8, 8, 10), [BgAlt] = C(28, 28, 32), [BgSelected] = C(68, 48, 110), [BgSelectedDim] = C(38, 34, 50),
        [Fg] = C(222, 222, 228), [FgDim] = C(122, 122, 134), [Accent] = C(176, 140, 255), [Good] = C(110, 210, 140), [Bad] = C(240, 100, 110),
        [Warn] = C(245, 195, 90), [HoverBg] = C(150, 150, 165), [HoverFg] = C(250, 250, 252),
        [Border] = C(44, 44, 52), [Faint] = C(62, 62, 72), [Marker] = C(92, 92, 108), [Thumb] = C(140, 140, 158),
    }));

    /// <summary>Deep navy with a teal accent.</summary>
    public static ThemeDefinition Midnight { get; } = new("Midnight", new ThemePalette(new Dictionary<ThemeSlot, Color>
    {
        [Bg] = C(10, 16, 36), [BgHeader] = C(5, 9, 24), [BgAlt] = C(22, 32, 62), [BgSelected] = C(36, 70, 140), [BgSelectedDim] = C(26, 40, 80),
        [Fg] = C(205, 216, 240), [FgDim] = C(110, 125, 160), [Accent] = C(80, 220, 210), [Good] = C(110, 215, 150), [Bad] = C(235, 100, 115),
        [Warn] = C(245, 200, 100), [HoverBg] = C(140, 155, 190), [HoverFg] = C(245, 248, 255),
        [Border] = C(34, 46, 84), [Faint] = C(52, 66, 108), [Marker] = C(80, 98, 146), [Thumb] = C(130, 148, 190),
    }));

    public static IReadOnlyList<ThemeDefinition> All { get; } = [Default, Obsidian, Midnight, Nord, HighContrast, Light, Paper, Sunny];

    public static IReadOnlyList<string> Names { get; } = All.Select(t => t.Name).ToList();

    /// <summary>The theme called <paramref name="name"/> (any case), or <see cref="Default"/> when there is none.</summary>
    public static ThemeDefinition Find(string? name) => All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Default;

    public static bool IsPredefined(string? name) => All.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The colours the settings ask for: the chosen theme (predefined or saved) with the user's overrides on top.</summary>
    public static ThemePalette Resolve(ThemeSettings settings)
    {
        var palette = settings.FindSaved(settings.Name) is { } saved ? Find(saved.Base).Palette.With(saved.Colors) : Find(settings.Name).Palette;
        return palette.With(settings.Colors);
    }
}
