using Clims.Catalog;
using Clims.Tui.Ui;

namespace Clims.Tui.Data;

/// <summary>One row of the player's queue.</summary>
/// <param name="Index">Position in the queue, from 0.</param>
/// <param name="Track">The track (with its file URL when the server sent it).</param>
/// <param name="Album">Its album (from the catalogue when known, otherwise built from the track's own fields).</param>
public sealed record QueueEntry(int Index, CatalogTrack Track, CatalogAlbum Album);
