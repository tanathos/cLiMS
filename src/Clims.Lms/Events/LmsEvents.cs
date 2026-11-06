namespace Clims.Lms;

/// <summary>
/// Something that happened on the server or on a player. Events are hints: after a queue or mode event, re-query
/// <c>status</c> instead of trusting the event's payload (the CLI sends text like <c>ARRAY(0x...)</c> for lists).
/// </summary>
public abstract record LmsEvent
{
    /// <summary>When the event was received by this client.</summary>
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>A new track started (natural advance or a jump). Treat the track position as 0 at this moment.</summary>
/// <param name="PlayerId">Player id.</param>
/// <param name="Index">Queue index of the new track, when reported.</param>
/// <param name="Title">Title as sent by the server.</param>
public sealed record TrackChangedEvent(string PlayerId, int? Index, string? Title) : LmsEvent;

/// <summary>LMS opened the next track on the player (about a second after a track starts: this is what makes gapless work).</summary>
/// <param name="PlayerId">Player id.</param>
/// <param name="Url">File URL of the track that was opened.</param>
public sealed record TrackOpenedEvent(string PlayerId, string Url) : LmsEvent;

/// <summary>Play, pause or stop. <see cref="Mode"/> is null for a bare <c>pause</c> toggle: re-query to learn the state.</summary>
/// <param name="PlayerId">Player id.</param>
/// <param name="Mode">New mode when known.</param>
public sealed record PlaybackStateEvent(string PlayerId, PlayMode? Mode) : LmsEvent;

/// <summary>The volume changed. <see cref="Volume"/> is null for a relative change (re-query).</summary>
/// <param name="PlayerId">Player id.</param>
/// <param name="Volume">Absolute volume 0 to 100 when known.</param>
public sealed record VolumeChangedEvent(string PlayerId, int? Volume) : LmsEvent;

/// <summary>The playback position was set (after a seek or jump).</summary>
/// <param name="PlayerId">Player id.</param>
/// <param name="Position">New position.</param>
public sealed record PositionChangedEvent(string PlayerId, TimeSpan Position) : LmsEvent;

/// <summary>The queue was edited (add, insert, delete, move, clear, load). Re-query <c>status</c>.</summary>
/// <param name="PlayerId">Player id.</param>
public sealed record QueueChangedEvent(string PlayerId) : LmsEvent;

/// <summary>Repeat or shuffle changed. Re-query <c>status</c>.</summary>
/// <param name="PlayerId">Player id.</param>
public sealed record ModeChangedEvent(string PlayerId) : LmsEvent;

/// <summary>Player power changed.</summary>
/// <param name="PlayerId">Player id.</param>
/// <param name="On">New power state.</param>
public sealed record PowerChangedEvent(string PlayerId, bool On) : LmsEvent;

/// <summary>A player connected to or disconnected from the server (a library scan ends with a reconnect).</summary>
/// <param name="PlayerId">Player id.</param>
/// <param name="Connected">True when connected.</param>
public sealed record PlayerConnectionEvent(string PlayerId, bool Connected) : LmsEvent;

/// <summary>A library scan finished. The server sends this twice, even when nothing changed; de-duplicated by the stream.</summary>
public sealed record ScanFinishedEvent : LmsEvent;

/// <summary>The server announced that the library changed.</summary>
public sealed record LibraryChangedEvent : LmsEvent;

/// <summary>Connection state of the event stream itself (so the UI can show an offline state).</summary>
/// <param name="Connected">True once subscribed, false when the socket dropped.</param>
/// <param name="Reason">Why it dropped, when known.</param>
public sealed record EventStreamStateEvent(bool Connected, string? Reason = null) : LmsEvent;

/// <summary>A source of <see cref="LmsEvent"/>s: the CLI socket, or a polling fallback.</summary>
public interface ILmsEventStream
{
    /// <summary>
    /// Streams events until cancelled. It never completes by itself: a dropped connection is retried with back-off
    /// and reported through <see cref="EventStreamStateEvent"/>. Use one enumerator at a time.
    /// </summary>
    IAsyncEnumerable<LmsEvent> ReadAllAsync(CancellationToken cancellationToken = default);
}
