namespace Clims.Catalog;

/// <summary>What the index knows about the server state it was built from. Compared with the live server to detect changes.</summary>
/// <param name="ServerUuid">Server UUID (the cache file is keyed by it).</param>
/// <param name="LastScan">Server <c>lastscan</c> as Unix seconds, null when never scanned.</param>
/// <param name="TrackCount">Songs when the sync started.</param>
/// <param name="AlbumCount">Albums when the sync started.</param>
/// <param name="ArtistCount">Artists when the sync started.</param>
/// <param name="GenreCount">Genres when the sync started.</param>
/// <param name="SyncedAt">When the sync finished.</param>
public sealed record CatalogMeta(
    string ServerUuid,
    long? LastScan,
    int TrackCount,
    int AlbumCount,
    int ArtistCount,
    int GenreCount,
    DateTimeOffset SyncedAt);
