using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Clims.Catalog;

/// <summary>Persistence of the catalogue. The in-memory <see cref="Catalog"/> does the searching; this only saves and reloads.</summary>
public interface ICatalogStore
{
    /// <summary>Loads the saved index, or null when there is none, it is unreadable, or it was written by another schema version.</summary>
    Catalog? Load();

    /// <summary>Replaces the saved index with <paramref name="catalog"/> in one transaction: a failure leaves the old index intact.</summary>
    void Save(Catalog catalog);

    /// <summary>Replaces the saved tracks of one album (on-view refresh).</summary>
    void ReplaceAlbumTracks(int albumId, IReadOnlyList<CatalogTrack> tracks);
}
