using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>Identifies a cover: the server's artwork id, or (demo data, no id) a seed for a generated one.</summary>
/// <param name="ArtworkId">Server artwork id; null when the album has none.</param>
/// <param name="Seed">Seed for generated demo covers.</param>
public readonly record struct CoverKey(string? ArtworkId, int Seed, bool Big = false);
