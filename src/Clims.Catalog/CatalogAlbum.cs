namespace Clims.Catalog;

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
