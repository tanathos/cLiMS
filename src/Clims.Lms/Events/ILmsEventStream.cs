namespace Clims.Lms;

/// <summary>A source of <see cref="LmsEvent"/>s: the CLI socket, or a polling fallback.</summary>
public interface ILmsEventStream
{
    /// <summary>
    /// Streams events until cancelled. It never completes by itself: a dropped connection is retried with back-off
    /// and reported through <see cref="EventStreamStateEvent"/>. Use one enumerator at a time.
    /// </summary>
    IAsyncEnumerable<LmsEvent> ReadAllAsync(CancellationToken cancellationToken = default);
}
