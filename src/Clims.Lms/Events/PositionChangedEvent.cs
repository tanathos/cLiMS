namespace Clims.Lms;

/// <summary>The playback position was set (after a seek or jump).</summary>
/// <param name="PlayerId">Player id.</param>
/// <param name="Position">New position.</param>
public sealed record PositionChangedEvent(string PlayerId, TimeSpan Position) : LmsEvent;
