namespace Clims.Tui.Settings;

/// <summary>The "UI" node: how things look.</summary>
public sealed record UiSettings
{
    public CoverStylesSettings Covers { get; init; } = new();

    public WaveformSettings Waveform { get; init; } = new();

    public ThemeSettings Theme { get; init; } = new();

    /// <summary>False: no version number under the volume bar.</summary>
    public bool ShowVersion { get; init; } = true;
}
