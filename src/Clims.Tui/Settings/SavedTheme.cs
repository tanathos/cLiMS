namespace Clims.Tui.Settings;

/// <summary>A theme the user saved under a name: a predefined theme and the colours that differ from it.</summary>
public sealed record SavedTheme
{
    /// <summary>The predefined theme the colours are changes of.</summary>
    public string Base { get; init; } = "Default";

    /// <summary>Slot name → <c>#RRGGBB</c>.</summary>
    public Dictionary<string, string> Colors { get; init; } = new();
}
