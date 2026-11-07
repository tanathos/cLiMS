namespace Clims.Lms;

/// <summary>Player power changed.</summary>
/// <param name="PlayerId">Player id.</param>
/// <param name="On">New power state.</param>
public sealed record PowerChangedEvent(string PlayerId, bool On) : LmsEvent;
