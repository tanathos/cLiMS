namespace Clims.Lms;

/// <summary>Repeat or shuffle changed. Re-query <c>status</c>.</summary>
/// <param name="PlayerId">Player id.</param>
public sealed record ModeChangedEvent(string PlayerId) : LmsEvent;
