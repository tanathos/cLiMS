using Clims.Catalog;
using Clims.Tui.Ui;

namespace Clims.Tui.Data;

/// <summary>The track a player is on, with its album (for the cover and names).</summary>
/// <param name="Album">Album of the track.</param>
/// <param name="Track">The track.</param>
/// <param name="IsRemote">True for a stream that is not a file of the library (radio, Spotify…): there is no waveform to analyse.</param>
/// <param name="Station">The name of the radio station when it is a radio stream, else null.</param>
/// <param name="Source">The streaming service the track comes from (Spotify, TIDAL…), else null.</param>
public sealed record NowPlaying(CatalogAlbum Album, CatalogTrack Track, bool IsRemote = false, string? Station = null, string? Source = null);
