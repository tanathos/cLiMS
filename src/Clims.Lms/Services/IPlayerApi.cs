namespace Clims.Lms;

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
