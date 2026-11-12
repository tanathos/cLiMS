using Clims.Tui.Ui;

namespace Clims.Tui.Settings;

/// <summary>The "UI.Waveform" node.</summary>
public sealed record WaveformSettings
{
    /// <summary>False: no waveform in the footer.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The style at start-up. Clicking the waveform switches it for the session.</summary>
    public WaveformStyle Style { get; init; } = WaveformStyle.Blocks;
}
