namespace Clims.Catalog;

/// <summary>One group of search hits, best first, with the number of matches before the list was cut.</summary>
/// <typeparam name="T">Hit type.</typeparam>
/// <param name="Items">The best hits (at most the limit that was asked for).</param>
/// <param name="Total">How many items matched in all.</param>
public sealed record SearchGroup<T>(IReadOnlyList<T> Items, int Total)
{
    /// <summary>A group with no hits.</summary>
    public static SearchGroup<T> None { get; } = new(Array.Empty<T>(), 0);
}

/// <summary>Ranked result of a search: four groups plus the folded query words (used to highlight matches).</summary>
/// <param name="Artists">Matching artists.</param>
/// <param name="Albums">Matching albums.</param>
/// <param name="Tracks">Matching tracks.</param>
/// <param name="Genres">Matching genres.</param>
/// <param name="Words">The query words, case and accents folded.</param>
/// <param name="TrackExtras">For tracks that came from the server: their album title and artwork id (the index may not know the album yet), by track id.</param>
public sealed record RankedSearchResult(
    SearchGroup<CatalogArtist> Artists,
    SearchGroup<CatalogAlbum> Albums,
    SearchGroup<CatalogTrack> Tracks,
    SearchGroup<CatalogGenre> Genres,
    IReadOnlyList<string> Words,
    IReadOnlyDictionary<int, TrackExtra>? TrackExtras = null)
{
    /// <summary>No query, or nothing to search.</summary>
    public static RankedSearchResult Empty { get; } = new(
        SearchGroup<CatalogArtist>.None,
        SearchGroup<CatalogAlbum>.None,
        SearchGroup<CatalogTrack>.None,
        SearchGroup<CatalogGenre>.None,
        Array.Empty<string>());

    /// <summary>True when no group has a hit.</summary>
    public bool IsEmpty => Artists.Total + Albums.Total + Tracks.Total + Genres.Total == 0;
}

/// <summary>What a server search knows about a track beyond <see cref="CatalogTrack"/>.</summary>
/// <param name="AlbumTitle">Album title.</param>
/// <param name="ArtworkId">Id for the cover art URL.</param>
public sealed record TrackExtra(string? AlbumTitle, string? ArtworkId);

/// <summary>Which groups a search had to ask the server for.</summary>
[Flags]
public enum SearchGroups
{
    /// <summary>All groups came from the local index.</summary>
    None = 0,

    /// <summary>Artists.</summary>
    Artists = 1,

    /// <summary>Albums.</summary>
    Albums = 2,

    /// <summary>Tracks.</summary>
    Tracks = 4,

    /// <summary>Genres.</summary>
    Genres = 8,

    /// <summary>Everything.</summary>
    All = Artists | Albums | Tracks | Genres,
}

/// <summary>What the search service reports after each keystroke, and again when the server has answered.</summary>
/// <param name="Query">The text that was searched.</param>
/// <param name="Result">The (possibly mixed) result.</param>
/// <param name="ServerGroups">Groups that are not in the local index and so came, or will come, from the server.</param>
/// <param name="ServerPending">True while the server's answer for <see cref="ServerGroups"/> is still on its way.</param>
/// <param name="Note">Something the user should know (the server could not be reached); null when all is well.</param>
public sealed record SearchOutcome(string Query, RankedSearchResult Result, SearchGroups ServerGroups, bool ServerPending, string? Note);
