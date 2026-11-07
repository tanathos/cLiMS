using Clims.Catalog;
using Clims.Tui.Ui;

namespace Clims.Tui.Data;

/// <summary>The track a player is on, with its album (for the cover and names).</summary>
/// <param name="Album">Album of the track.</param>
/// <param name="Track">The track.</param>
public sealed record NowPlaying(CatalogAlbum Album, CatalogTrack Track);
