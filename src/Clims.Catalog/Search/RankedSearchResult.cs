namespace Clims.Catalog;

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
