namespace Clims.Lms;

/// <summary>Sort orders of <c>albums</c>.</summary>
public enum AlbumSort
{
    /// <summary>By album title.</summary>
    Album,

    /// <summary>By artist, then album title.</summary>
    ArtistAlbum,

    /// <summary>By year, then album title.</summary>
    YearAlbum,

    /// <summary>By year, artist, album.</summary>
    YearArtistAlbum,

    /// <summary>Newest added first.</summary>
    New,

    /// <summary>Random order (a different order on every call).</summary>
    Random,
}

/// <summary>Sort orders of <c>titles</c>.</summary>
public enum TrackSort
{
    /// <summary>By title.</summary>
    Title,

    /// <summary>By track number (use with an album filter).</summary>
    TrackNumber,

    /// <summary>By album, then track number.</summary>
    AlbumTrack,

    /// <summary>Newest added first.</summary>
    New,

    /// <summary>Random order.</summary>
    Random,
}

/// <summary>Filters for artist listings.</summary>
public sealed record ArtistQuery
{
    /// <summary>Only artists with this genre.</summary>
    public int? GenreId { get; init; }

    /// <summary>Word-prefix search text.</summary>
    public string? Search { get; init; }
}

/// <summary>Filters for album listings.</summary>
public sealed record AlbumQuery
{
    /// <summary>Only albums of this artist.</summary>
    public int? ArtistId { get; init; }

    /// <summary>Only albums with this genre.</summary>
    public int? GenreId { get; init; }

    /// <summary>Only albums from this year.</summary>
    public int? Year { get; init; }

    /// <summary>Word-prefix search text.</summary>
    public string? Search { get; init; }

    /// <summary>Restrict to compilations (true) or exclude them (false).</summary>
    public bool? Compilation { get; init; }

    /// <summary>Sort order.</summary>
    public AlbumSort Sort { get; init; } = AlbumSort.Album;

    /// <summary>Fields to request.</summary>
    public AlbumTags Tags { get; init; } = AlbumTags.List;
}

/// <summary>Filters for track listings. At least one filter should be set: the library has tens of thousands of tracks.</summary>
public sealed record TrackQuery
{
    /// <summary>Only tracks of this album (overrides other filters on the server).</summary>
    public int? AlbumId { get; init; }

    /// <summary>Only tracks of this artist.</summary>
    public int? ArtistId { get; init; }

    /// <summary>Only tracks with this genre.</summary>
    public int? GenreId { get; init; }

    /// <summary>Word-prefix search text.</summary>
    public string? Search { get; init; }

    /// <summary>Sort order.</summary>
    public TrackSort Sort { get; init; } = TrackSort.AlbumTrack;

    /// <summary>Fields to request.</summary>
    public TrackTags Tags { get; init; } = TrackTags.List;
}

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

/// <summary>Transport and player state for one player (identified by id in every call).</summary>
public interface IPlayerApi
{
    /// <summary>
    /// Reads the player state and a window of its queue. With <paramref name="start"/> null the window starts at the current track.
    /// </summary>
    Task<PlayerStatus> GetStatusAsync(string playerId, int? start = null, int count = 1, TrackTags tags = TrackTags.List, CancellationToken cancellationToken = default);

    /// <summary>Starts or resumes playback.</summary>
    Task PlayAsync(string playerId, CancellationToken cancellationToken = default);

    /// <summary>Pauses (true), resumes (false) or toggles (null).</summary>
    Task PauseAsync(string playerId, bool? pause = null, CancellationToken cancellationToken = default);

    /// <summary>Stops playback. Not gapless; do not use it to advance.</summary>
    Task StopAsync(string playerId, CancellationToken cancellationToken = default);

    /// <summary>Seeks to an absolute position in the current track.</summary>
    Task SeekAsync(string playerId, TimeSpan position, CancellationToken cancellationToken = default);

    /// <summary>Seeks relative to the current position (negative goes back).</summary>
    Task SeekByAsync(string playerId, TimeSpan delta, CancellationToken cancellationToken = default);

    /// <summary>Sets the volume, clamped to 0 to 100.</summary>
    Task SetVolumeAsync(string playerId, int volume, CancellationToken cancellationToken = default);

    /// <summary>Changes the volume by a delta, clamped by the server.</summary>
    Task AdjustVolumeAsync(string playerId, int delta, CancellationToken cancellationToken = default);

    /// <summary>Mutes or unmutes.</summary>
    Task SetMutedAsync(string playerId, bool muted, CancellationToken cancellationToken = default);

    /// <summary>Plays the next queue entry. A manual skip re-buffers; natural advance is gapless.</summary>
    Task NextAsync(string playerId, CancellationToken cancellationToken = default);

    /// <summary>Plays the previous queue entry.</summary>
    Task PreviousAsync(string playerId, CancellationToken cancellationToken = default);

    /// <summary>Jumps to a queue index.</summary>
    Task JumpToAsync(string playerId, int index, CancellationToken cancellationToken = default);

    /// <summary>Sets the repeat mode.</summary>
    Task SetRepeatAsync(string playerId, RepeatMode mode, CancellationToken cancellationToken = default);

    /// <summary>Sets the shuffle mode.</summary>
    Task SetShuffleAsync(string playerId, ShuffleMode mode, CancellationToken cancellationToken = default);

    /// <summary>Turns the player on or off.</summary>
    Task SetPowerAsync(string playerId, bool on, CancellationToken cancellationToken = default);
}

/// <summary>
/// Editing a player's queue in place. Everything here except the <c>Load*</c> methods keeps playback going without a gap;
/// <c>Load*</c> replaces the queue and restarts the stream. Id lists are split into several requests automatically.
/// </summary>
public interface IQueueApi
{
    /// <summary>Reads a window of the queue.</summary>
    Task<Page<QueueItem>> GetQueueAsync(string playerId, int start = 0, int count = 100, TrackTags tags = TrackTags.List, CancellationToken cancellationToken = default);

    /// <summary>Replaces the queue with these tracks and starts playing at <paramref name="playIndex"/>. Not gapless.</summary>
    Task LoadTracksAsync(string playerId, IReadOnlyList<int> trackIds, int playIndex = 0, CancellationToken cancellationToken = default);

    /// <summary>Appends tracks after the last queue entry (gapless).</summary>
    Task AddTracksAsync(string playerId, IReadOnlyList<int> trackIds, CancellationToken cancellationToken = default);

    /// <summary>Inserts tracks right after the current track, keeping their order ("play next", gapless).</summary>
    Task InsertTracksNextAsync(string playerId, IReadOnlyList<int> trackIds, CancellationToken cancellationToken = default);

    /// <summary>Replaces the queue with an album and starts at <paramref name="playIndex"/>. Not gapless.</summary>
    Task LoadAlbumAsync(string playerId, int albumId, int playIndex = 0, CancellationToken cancellationToken = default);

    /// <summary>Appends an album (gapless).</summary>
    Task AddAlbumAsync(string playerId, int albumId, CancellationToken cancellationToken = default);

    /// <summary>Inserts an album after the current track (gapless).</summary>
    Task InsertAlbumNextAsync(string playerId, int albumId, CancellationToken cancellationToken = default);

    /// <summary>Removes entries by queue index. Indices refer to the queue before the call.</summary>
    Task RemoveAsync(string playerId, IReadOnlyCollection<int> indices, CancellationToken cancellationToken = default);

    /// <summary>Moves one entry from one index to another.</summary>
    Task MoveAsync(string playerId, int fromIndex, int toIndex, CancellationToken cancellationToken = default);

    /// <summary>Empties the queue and stops playback.</summary>
    Task ClearAsync(string playerId, CancellationToken cancellationToken = default);
}

/// <summary>Server-wide information.</summary>
public interface IServerApi
{
    /// <summary>Server version text, for example 9.1.1.</summary>
    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>Version, library totals, last scan time and players in one call.</summary>
    Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists players.</summary>
    Task<IReadOnlyList<PlayerInfo>> GetPlayersAsync(CancellationToken cancellationToken = default);

    /// <summary>Current scan progress. Back off from heavy queries while a scan runs.</summary>
    Task<ScanState> GetScanStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Total tracks in the library: a cheap poll to detect library changes.</summary>
    Task<int> GetTrackCountAsync(CancellationToken cancellationToken = default);

    /// <summary>Asks the server whether a command exists (<c>can playlistcontrol ?</c>).</summary>
    Task<bool> CanAsync(IReadOnlyList<string> command, CancellationToken cancellationToken = default);
}

/// <summary>Entry point: the typed APIs plus the raw client for commands that are not wrapped.</summary>
public interface ILmsClient
{
    /// <summary>Base address of the server, used to build art and download URLs.</summary>
    Uri BaseUri { get; }

    /// <summary>Raw JSON-RPC access.</summary>
    ILmsRpcClient Rpc { get; }

    /// <summary>Library queries.</summary>
    ILibraryApi Library { get; }

    /// <summary>Transport and status.</summary>
    IPlayerApi Player { get; }

    /// <summary>Queue editing.</summary>
    IQueueApi Queue { get; }

    /// <summary>Server information.</summary>
    IServerApi Server { get; }
}
