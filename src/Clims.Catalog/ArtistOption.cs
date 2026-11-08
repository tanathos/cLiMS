namespace Clims.Catalog;

/// <summary>An artist with tracks matching the explorer's selection.</summary>
/// <param name="Name">Track artist name.</param>
/// <param name="Tracks">Matching tracks by this artist.</param>
/// <param name="AlbumId">One album with a matching track (to open the artist in the Library).</param>
public sealed record ArtistOption(string Name, int Tracks, int AlbumId);
