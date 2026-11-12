namespace Clims.Tui.Settings;

/// <summary>What a row of the settings page is, and so how it is changed.</summary>
public enum SettingKind
{
    /// <summary>On or off. Space, Enter, Left or Right flips it.</summary>
    Toggle,

    /// <summary>One of a few named values. Left and Right go through them.</summary>
    Choice,

    /// <summary>A number within limits. Left and Right step it.</summary>
    Number,

    /// <summary>Free text, typed in after Enter.</summary>
    Text,

    /// <summary>A server of the list.</summary>
    Server,

    /// <summary>Does something when Enter is pressed (adds a server).</summary>
    Action,

    /// <summary>A line of information; it cannot be changed here.</summary>
    Info,
}
