using System.Threading.Channels;
using Clims.Lms;

namespace Clims.Catalog.Tests;

/// <summary>An event stream a test pushes events into.</summary>
public sealed class FakeEvents : ILmsEventStream
{
    private readonly Channel<LmsEvent> _channel = Channel.CreateUnbounded<LmsEvent>();

    public void Push(LmsEvent e) => _channel.Writer.TryWrite(e);

    public void Complete() => _channel.Writer.TryComplete();

    public IAsyncEnumerable<LmsEvent> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
