namespace Clims.Lms;

/// <summary>A new track started (natural advance or a jump). Treat the track position as 0 at this moment.</summary>
/// <param name="PlayerId">Player id.</param>
/// <param name="Index">Queue index of the new track, when reported.</param>
/// <param name="Title">Title as sent by the server.</param>
public sealed record TrackChangedEvent(string PlayerId, int? Index, string? Title) : LmsEvent;
