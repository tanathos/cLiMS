namespace Clims.Lms;

/// <summary>The queue was edited (add, insert, delete, move, clear, load). Re-query <c>status</c>.</summary>
/// <param name="PlayerId">Player id.</param>
public sealed record QueueChangedEvent(string PlayerId) : LmsEvent;
