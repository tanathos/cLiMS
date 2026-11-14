namespace Clims.Info;

/// <summary>
/// A source of artist and album information. Providers are asked in order by the <see cref="InfoService"/>, and the first one that
/// knows the artist or album wins. A provider returns null when it does not know what was asked, and throws
/// <see cref="HttpRequestException"/> when it could not be reached (which says nothing about the artist).
/// </summary>
public interface IInfoProvider
{
    /// <summary>Short name, shown to the user and stored with the result ("musicbrainz").</summary>
    string Name { get; }

    /// <summary>What the provider knows about the artist of <paramref name="query"/>.</summary>
    /// <param name="query">What to look up.</param>
    /// <param name="ct">Cancels the request.</param>
    Task<ArtistInfo?> GetArtistAsync(InfoQuery query, CancellationToken ct);

    /// <summary>What the provider knows about the album of <paramref name="query"/>.</summary>
    /// <param name="query">What to look up (with the artist's id when the artist was found).</param>
    /// <param name="ct">Cancels the request.</param>
    Task<AlbumInfo?> GetAlbumAsync(InfoQuery query, CancellationToken ct);
}
