using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Clims.Lms;
using Clims.Tui.Data;
using Clims.Tui.Ui;
using Clims.Catalog;
using MusicCatalog = Clims.Catalog.Catalog;

namespace Clims.Tui.Tests;

internal sealed class FakeEventStream : ILmsEventStream
{
    private readonly Channel<LmsEvent> _channel = Channel.CreateUnbounded<LmsEvent>();

    public void Push(LmsEvent e) => _channel.Writer.TryWrite(e);

    public IAsyncEnumerable<LmsEvent> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
