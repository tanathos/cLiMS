namespace Clims.Lms;

/// <summary>
/// Something that happened on the server or on a player. Events are hints: after a queue or mode event, re-query
/// <c>status</c> instead of trusting the event's payload (the CLI sends text like <c>ARRAY(0x...)</c> for lists).
/// </summary>
public abstract record LmsEvent
{
    /// <summary>When the event was received by this client.</summary>
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}
