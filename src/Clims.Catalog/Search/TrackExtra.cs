namespace Clims.Catalog;

/// <summary>What a server search knows about a track beyond <see cref="CatalogTrack"/>.</summary>
/// <param name="AlbumTitle">Album title.</param>
/// <param name="ArtworkId">Id for the cover art URL.</param>
public sealed record TrackExtra(string? AlbumTitle, string? ArtworkId);
