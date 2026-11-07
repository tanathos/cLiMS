using System.Threading.Channels;
using Clims.Lms;

namespace Clims.Catalog.Tests;

/// <summary>A store that keeps the catalogue in memory and counts how it is used.</summary>
public sealed class MemoryStore : ICatalogStore
{
    public Catalog? Saved { get; set; }

    public int SaveCount { get; private set; }

    public Dictionary<int, IReadOnlyList<CatalogTrack>> ReplacedAlbums { get; } = new();

    public Catalog? Load() => Saved;

    public void Save(Catalog catalog)
    {
        SaveCount++;
        Saved = catalog;
    }

    public void ReplaceAlbumTracks(int albumId, IReadOnlyList<CatalogTrack> tracks) => ReplacedAlbums[albumId] = tracks;
}
