namespace Clims.Lms;

/// <summary>Play, pause or stop. <see cref="Mode"/> is null for a bare <c>pause</c> toggle: re-query to learn the state.</summary>
/// <param name="PlayerId">Player id.</param>
/// <param name="Mode">New mode when known.</param>
public sealed record PlaybackStateEvent(string PlayerId, PlayMode? Mode) : LmsEvent;
