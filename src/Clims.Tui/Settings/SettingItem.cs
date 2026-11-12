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

    /// <summary>Text rows: an empty text is accepted (it means "none" or "the default").</summary>
    public bool AllowEmpty { get; init; }

    /// <summary>Text rows: why the typed text is not acceptable, or null when it is.</summary>
    public Func<string, string?>? Validate { get; init; }

    /// <summary>Action rows: the change to make. An action row without one asks for a server to be added.</summary>
    public Func<UserSettings, UserSettings>? Run { get; init; }

    /// <summary>A colour shown as a block before the value.</summary>
    public Func<UserSettings, Terminal.Gui.Drawing.Color>? Swatch { get; init; }

    /// <summary>Server rows: its UUID, or its address when the UUID is not known.</summary>
    public string? ServerKey { get; init; }

    /// <summary>Server rows: whether the connection is open to this server now.</summary>
    public bool Connected { get; init; }
}
