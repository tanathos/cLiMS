namespace Clims.Lms;

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
