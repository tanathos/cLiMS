namespace Clims.Tui.Settings;

/// <summary>The "UI" node: how things look.</summary>
public sealed record UiSettings
{
    public CoverStylesSettings Covers { get; init; } = new();

    public WaveformSettings Waveform { get; init; } = new();
}
