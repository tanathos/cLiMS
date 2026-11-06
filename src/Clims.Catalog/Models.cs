namespace Clims.Catalog;

/// <summary>An album artist as listed by the server. Albums refer to it by <see cref="Id"/>.</summary>
/// <param name="Id">LMS artist id (can change after a full rescan; a resync refreshes it).</param>
/// <param name="Name">Display name.</param>
public sealed record CatalogArtist(int Id, string Name)
{
    /// <summary>
    /// The A-Z jump key: first letter ignoring a leading "The ", digits grouped as <c>#</c> and everything else as <c>#</c> too.
    /// </summary>
    public string TextKey => TextKeys.For(Name);
}

/// <summary>A genre.</summary>
/// <param name="Id">LMS genre id.</param>
/// <param name="Name">Genre name.</param>
public sealed record CatalogGenre(int Id, string Name);

/// <summary>An album row of the index.</summary>
/// <param name="Id">LMS album id.</param>
/// <param name="Title">Album title.</param>
/// <param name="ArtistId">Album artist id, when known.</param>
/// <param name="ArtistName">Album artist name.</param>
/// <param name="Year">Release year, 0 when unknown.</param>
/// <param name="ArtworkId">Id for cover art URLs; null when the album has no artwork.</param>
/// <param name="IsCompilation">True for compilations.</param>
/// <param name="DiscCount">Number of discs when known.</param>
public sealed record CatalogAlbum(
    int Id,
    string Title,
    int? ArtistId,
    string ArtistName,
    int Year,
    string? ArtworkId,
    bool IsCompilation,
    int? DiscCount);

/// <summary>A track row of the index (a light set of fields; the rich set is fetched on demand).</summary>
/// <param name="Id">LMS track id.</param>
/// <param name="AlbumId">Owning album.</param>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Track artist name.</param>
/// <param name="ArtistId">Track artist id.</param>
/// <param name="Disc">Disc number (1 when the album has a single disc or the server sent none).</param>
/// <param name="Number">Track number on the disc (0 when unknown).</param>
/// <param name="Duration">Length.</param>
/// <param name="Genre">Genre name.</param>
/// <param name="Url">File URL, the durable key across rescans; only present after an on-view refresh.</param>
public sealed record CatalogTrack(
    int Id,
    int AlbumId,
    string Title,
    string? Artist,
    int? ArtistId,
    int Disc,
    int Number,
    TimeSpan Duration,
    string? Genre,
    string? Url = null);

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

/// <summary>Derived facts of an album for display.</summary>
/// <param name="TrackCount">Tracks known for the album (0 until they are loaded).</param>
/// <param name="Duration">Total length of the known tracks.</param>
/// <param name="Genres">Distinct genres of the known tracks, most frequent first.</param>
public sealed record AlbumSummary(int TrackCount, TimeSpan Duration, IReadOnlyList<string> Genres);

internal static class TextKeys
{
    public static string For(string name)
    {
        var s = name.AsSpan().TrimStart();
        if (s.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
        {
            s = s[4..].TrimStart();
        }

        if (s.IsEmpty)
        {
            return "#";
        }

        var folded = TextFolding.Fold(s[..1].ToString());
        return folded.Length > 0 && char.IsLetter(folded[0]) ? char.ToUpperInvariant(folded[0]).ToString() : "#";
    }
}
