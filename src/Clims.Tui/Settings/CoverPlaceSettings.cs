using System.Text.Json.Serialization;
using Clims.Tui.Ui;

namespace Clims.Tui.Settings;

/// <summary>How the covers of one place in the UI are drawn.</summary>
public sealed record CoverPlaceSettings
{
    /// <summary>The drawing style. Sixel falls back to <see cref="CoverStylesSettings.SixelFallback"/> on terminals without it.</summary>
    public CoverMode Style { get; init; } = CoverMode.Sixel;

    /// <summary>The cover size. Only the library and recently added grids use it; other places leave it out of the file.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CoverSize? Size { get; init; }
}
