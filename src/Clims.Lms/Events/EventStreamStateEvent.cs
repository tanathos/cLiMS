namespace Clims.Lms;

/// <summary>Connection state of the event stream itself (so the UI can show an offline state).</summary>
/// <param name="Connected">True once subscribed, false when the socket dropped.</param>
/// <param name="Reason">Why it dropped, when known.</param>
public sealed record EventStreamStateEvent(bool Connected, string? Reason = null) : LmsEvent;
