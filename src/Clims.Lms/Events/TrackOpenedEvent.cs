namespace Clims.Lms;

/// <summary>LMS opened the next track on the player (about a second after a track starts: this is what makes gapless work).</summary>
/// <param name="PlayerId">Player id.</param>
/// <param name="Url">File URL of the track that was opened.</param>
public sealed record TrackOpenedEvent(string PlayerId, string Url) : LmsEvent;
