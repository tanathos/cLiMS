using Clims.Tui.Ui;

namespace Clims.Tui.Settings;

/// <summary>The "UI.Visualizer" node: the live spectrum fed by vismux on the player.</summary>
public sealed record VisualizerSettings
{
    /// <summary>Address of the player running vismux (next to Squeezelite started with <c>-v</c>). Empty: no live spectrum.</summary>
    public string Host { get; init; } = "";

    /// <summary>The spectrum style at start-up. F9 or a click on the spectrum switches it for the session.</summary>
    public SpectrumStyle Style { get; init; } = SpectrumStyle.Bars;

    /// <summary>True: the full-screen album view also shows the waveform or spectrum, above the progress bars and under the lyrics.</summary>
    public bool FullScreen { get; init; }

    /// <summary>The port vismux listens on.</summary>
    public int Port { get; init; } = 23483;
}
