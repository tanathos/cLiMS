namespace Clims.Catalog;

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
