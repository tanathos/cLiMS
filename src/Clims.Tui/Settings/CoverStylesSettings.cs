using Clims.Tui.Ui;

namespace Clims.Tui.Settings;

/// <summary>The "UI.Covers" node: one style for each place that shows covers.</summary>
public sealed record CoverStylesSettings
{
    /// <summary>The player footer.</summary>
    public CoverPlaceSettings Footer { get; init; } = new();

    /// <summary>The library and recently added grids (the only place with a size).</summary>
    public CoverPlaceSettings Library { get; init; } = new() { Size = CoverSize.Medium };

    /// <summary>The full-screen cover.</summary>
    public CoverPlaceSettings FullScreen { get; init; } = new();

    /// <summary>The style used where Sixel is chosen but the terminal cannot do it.</summary>
    public CoverMode SixelFallback { get; init; } = CoverMode.Quadrant;
}
