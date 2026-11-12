using Clims.Tui.Ui;

namespace Clims.Tui.Settings;

/// <summary>The "UI.Theme" node: a predefined or saved theme and the colours the user changed on top of it.</summary>
public sealed record ThemeSettings
{
    /// <summary>A predefined theme (see <see cref="Themes.Names"/>) or the name of a saved one; an unknown name gives the default theme.</summary>
    public string Name { get; init; } = Themes.Default.Name;

    /// <summary>Colour overrides on top of the theme: slot name (see <see cref="ThemeSlot"/>) → <c>#RRGGBB</c>.</summary>
    public Dictionary<string, string> Colors { get; init; } = new();

    /// <summary>The themes the user saved, by name.</summary>
    public Dictionary<string, SavedTheme> Saved { get; init; } = new();

    /// <summary>The override for a slot, or null.</summary>
    public string? Override(ThemeSlot slot) =>
        Colors.FirstOrDefault(c => string.Equals(c.Key, slot.ToString(), StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>The saved theme called <paramref name="name"/> (any case), or null. A predefined name is never a saved theme.</summary>
    public SavedTheme? FindSaved(string? name) =>
        Themes.IsPredefined(name) ? null : Saved.FirstOrDefault(s => string.Equals(s.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>The name of the theme in use as the pickers show it: a predefined or saved name, else the default's.</summary>
    public string DisplayName =>
        Themes.IsPredefined(Name) ? Themes.Find(Name).Name
        : Saved.Keys.FirstOrDefault(k => string.Equals(k, Name, StringComparison.OrdinalIgnoreCase)) ?? Themes.Default.Name;

    /// <summary>A copy with the slot overridden (<paramref name="hex"/> null or empty: the theme's own colour again).</summary>
    public ThemeSettings WithColor(ThemeSlot slot, string? hex)
    {
        var colors = Colors.Where(c => !string.Equals(c.Key, slot.ToString(), StringComparison.OrdinalIgnoreCase)).ToDictionary(c => c.Key, c => c.Value);
        if (!string.IsNullOrWhiteSpace(hex))
        {
            colors[slot.ToString()] = hex.Trim();
        }

        return this with { Colors = colors };
    }

    /// <summary>
    /// Keeps the theme in use, with the colour changes, under <paramref name="name"/> (an old saved theme of that name is replaced),
    /// and makes it the theme in use with no changes on top.
    /// </summary>
    public ThemeSettings SaveAs(string name)
    {
        name = name.Trim();
        var current = FindSaved(Name);
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (slot, hex) in current?.Colors ?? new())
        {
            colors[slot] = hex;
        }

        foreach (var (slot, hex) in Colors)
        {
            colors[slot] = hex;
        }

        var saved = Saved.Where(s => !string.Equals(s.Key, name, StringComparison.OrdinalIgnoreCase)).ToDictionary(s => s.Key, s => s.Value);
        saved[name] = new SavedTheme { Base = current?.Base ?? Themes.Find(Name).Name, Colors = colors.ToDictionary(c => c.Key, c => c.Value) };
        return this with { Name = name, Colors = new Dictionary<string, string>(), Saved = saved };
    }

    /// <summary>A copy without the saved theme in use (the theme it was made from takes its place); unchanged when the theme in use is not a saved one.</summary>
    public ThemeSettings DeleteSaved()
    {
        if (FindSaved(Name) is not { } saved)
        {
            return this;
        }

        var rest = Saved.Where(s => !string.Equals(s.Key, Name, StringComparison.OrdinalIgnoreCase)).ToDictionary(s => s.Key, s => s.Value);
        return this with { Name = Themes.Find(saved.Base).Name, Saved = rest };
    }
}
