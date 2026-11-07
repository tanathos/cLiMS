namespace Clims.Lms;

/// <summary>Reading the music library. All calls are idempotent and retried on connection failures.</summary>
public interface ILibraryApi
{
    /// <summary>Lists artists. The server returns all matches in one call, so a count above the total is fine.</summary>
    Task<Page<Artist>> GetArtistsAsync(ArtistQuery? query = null, int start = 0, int count = 5000, CancellationToken cancellationToken = default);

    /// <summary>Lists albums. <c>sort:changed</c> is not offered because the server caps it at 100.</summary>
    Task<Page<Album>> GetAlbumsAsync(AlbumQuery? query = null, int start = 0, int count = 5000, CancellationToken cancellationToken = default);

    /// <summary>Lists tracks. Page with at most about 2 000 per call; the server fails above roughly 5 000.</summary>
    Task<Page<Track>> GetTracksAsync(TrackQuery query, int start = 0, int count = 500, CancellationToken cancellationToken = default);

    /// <summary>Lists genres.</summary>
    Task<Page<Genre>> GetGenresAsync(int start = 0, int count = 5000, CancellationToken cancellationToken = default);

    /// <summary>The server's global word-prefix search across artists, albums, tracks and genres.</summary>
    Task<SearchResults> SearchAsync(string term, int count = 10, CancellationToken cancellationToken = default);

    /// <summary>Looks one track up by id; null when it does not exist.</summary>
    Task<Track?> GetTrackAsync(int trackId, TrackTags tags = TrackTags.All, CancellationToken cancellationToken = default);

    /// <summary>Looks one track up by file URL (the durable key); null when unknown.</summary>
    Task<Track?> GetTrackByUrlAsync(string url, TrackTags tags = TrackTags.All, CancellationToken cancellationToken = default);

    /// <summary>Lists saved server-side playlists (none saved gives an empty page).</summary>
    Task<Page<SavedPlaylist>> GetPlaylistsAsync(int start = 0, int count = 500, CancellationToken cancellationToken = default);
}
