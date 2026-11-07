namespace Clims.Lms;

/// <summary>A player connected to or disconnected from the server (a library scan ends with a reconnect).</summary>
/// <param name="PlayerId">Player id.</param>
/// <param name="Connected">True when connected.</param>
public sealed record PlayerConnectionEvent(string PlayerId, bool Connected) : LmsEvent;
