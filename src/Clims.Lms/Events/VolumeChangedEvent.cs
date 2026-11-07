namespace Clims.Lms;

/// <summary>The volume changed. <see cref="Volume"/> is null for a relative change (re-query).</summary>
/// <param name="PlayerId">Player id.</param>
/// <param name="Volume">Absolute volume 0 to 100 when known.</param>
public sealed record VolumeChangedEvent(string PlayerId, int? Volume) : LmsEvent;
