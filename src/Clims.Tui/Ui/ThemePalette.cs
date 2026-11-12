using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>One colour for every <see cref="ThemeSlot"/>. Immutable.</summary>
public sealed class ThemePalette
{
    private readonly Color[] _colors;

    /// <summary>Throws when a slot has no colour, so a theme cannot be left incomplete.</summary>
    public ThemePalette(IReadOnlyDictionary<ThemeSlot, Color> colors)
    {
        var slots = Enum.GetValues<ThemeSlot>();
        _colors = new Color[slots.Length];
        foreach (var slot in slots)
        {
            _colors[(int)slot] = colors.TryGetValue(slot, out var color)
                ? color
                : throw new ArgumentException($"The theme has no colour for {slot}.", nameof(colors));
        }
    }

    private ThemePalette(Color[] colors) => _colors = colors;

    public Color this[ThemeSlot slot] => _colors[(int)slot];

    /// <summary>The slot named <paramref name="name"/> (any case), or false when there is none.</summary>
    public static bool TryParseSlot(string? name, out ThemeSlot slot) => Enum.TryParse(name, ignoreCase: true, out slot) && Enum.IsDefined(slot);

    /// <summary>A copy with the colours in <paramref name="overrides"/> (slot name → #RRGGBB) applied. Unknown names and bad colours are ignored.</summary>
    public ThemePalette With(IReadOnlyDictionary<string, string>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
        {
            return this;
        }

        var colors = (Color[])_colors.Clone();
        foreach (var (name, value) in overrides)
        {
            if (TryParseSlot(name, out var slot) && HexColor.TryParse(value, out var color))
            {
                colors[(int)slot] = color;
            }
        }

        return new ThemePalette(colors);
    }
}
