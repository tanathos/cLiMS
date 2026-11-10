namespace Clims.Tui.Settings;

/// <summary>The "Startup" node.</summary>
public sealed record StartupSettings
{
    /// <summary>The server to open: its UUID, or its address when the UUID is not known. Null: the first one in the list.</summary>
    public string? Server { get; init; }

    /// <summary>The tab shown first.</summary>
    public StartupView View { get; init; } = StartupView.Library;

    /// <summary>True: connect to the server at once. False: show the connect dialog first.</summary>
    public bool AutoConnect { get; init; } = true;
}
