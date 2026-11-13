namespace Clims.Tui.Settings;

/// <summary>The "UI" node: how things look.</summary>
public sealed record UiSettings
{
    public CoverStylesSettings Covers { get; init; } = new();

    /// <summary>How the Library and Recently added tabs list albums: <see cref="AlbumLayout.List"/> or <see cref="AlbumLayout.Grid"/>.</summary>
    public AlbumLayout AlbumLayout { get; init; } = AlbumLayout.List;

    /// <summary>The background of the tracklist panel in the grid layout.</summary>
    public GridBackdrop GridBackground { get; init; } = GridBackdrop.Pixellated;

    public WaveformSettings Waveform { get; init; } = new();

    public ThemeSettings Theme { get; init; } = new();

    /// <summary>False: no version number under the volume bar.</summary>
    public bool ShowVersion { get; init; } = true;
}
