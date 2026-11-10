namespace Clims.Tui.Settings;

/// <summary>One row of the settings page: how it is shown and how it changes the settings. Built by <see cref="SettingItems"/>.</summary>
public sealed class SettingItem
{
    public required string Label { get; init; }

    public required SettingKind Kind { get; init; }

    /// <summary>What the row shows as its value.</summary>
    public required Func<UserSettings, string> Value { get; init; }

    /// <summary>A short explanation after the value.</summary>
    public string Hint { get; init; } = "";

    /// <summary>True when a change only takes effect at the next start (or reconnect).</summary>
    public bool NeedsRestart { get; init; }

    /// <summary>Toggle, choice and number: the settings after a step of -1 or +1.</summary>
    public Func<UserSettings, int, UserSettings>? Change { get; init; }

    /// <summary>Text rows: the text to start editing from.</summary>
    public Func<UserSettings, string>? EditText { get; init; }

    /// <summary>Text rows: the settings with the typed text applied.</summary>
    public Func<UserSettings, string, UserSettings>? Edit { get; init; }

    /// <summary>Server rows: its UUID, or its address when the UUID is not known.</summary>
    public string? ServerKey { get; init; }

    /// <summary>Server rows: whether the connection is open to this server now.</summary>
    public bool Connected { get; init; }
}
