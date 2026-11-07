using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace Clims.Lms;

/// <summary>Settings for <see cref="CliEventStream"/>.</summary>
public sealed class CliEventStreamOptions
{
    /// <summary>Server host name or address.</summary>
    public required string Host { get; init; }

    /// <summary>CLI port; 9090 by default.</summary>
    public int Port { get; init; } = 9090;

    /// <summary>First reconnect delay; doubled after every failed attempt up to <see cref="MaxReconnectDelay"/>.</summary>
    public TimeSpan MinReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Longest wait between reconnect attempts.</summary>
    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Identical lines closer together than this are reported once.</summary>
    public TimeSpan DedupeWindow { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How long a connect attempt may take.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Login for a server with password protection; sent as <c>login user password</c> after every connect. Never logged.</summary>
    public NetworkCredential? Credentials { get; init; }
}

/// <summary>
/// Push notifications from the LMS telnet CLI (port 9090, <c>listen 1</c>, no login needed on a server without
/// password protection). Reconnects with exponential back-off, percent-decodes, de-duplicates and drops noise;
/// see <see cref="LmsEventParser"/>.
/// </summary>
public sealed class CliEventStream : ILmsEventStream
{
    private readonly CliEventStreamOptions _options;
    private volatile bool _loginRejected;

    /// <summary>Creates the stream. Nothing connects until <see cref="ReadAllAsync"/> is enumerated.</summary>
    public CliEventStream(CliEventStreamOptions options)
    {
        _options = options;
    }

    /// <summary>True once the server dropped the connection right after the login: the stream then stops (retrying could lock the account).</summary>
    public bool LoginRejected => _loginRejected;

    /// <summary>The CLI line that logs in: both parts percent-encoded, as the CLI expects.</summary>
    internal static string LoginLine(NetworkCredential c) =>
        $"login {Uri.EscapeDataString(c.UserName)} {Uri.EscapeDataString(c.Password)}\n";

    /// <inheritdoc />
    public async IAsyncEnumerable<LmsEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<LmsEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pump = Task.Run(() => PumpAsync(channel.Writer, stop.Token), CancellationToken.None);
        try
        {
            await foreach (var e in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return e;
            }
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await pump.ConfigureAwait(false);
        }
    }

    private async Task PumpAsync(ChannelWriter<LmsEvent> output, CancellationToken ct)
    {
        var delay = _options.MinReconnectDelay;
        var deduper = new LineDeduper(_options.DedupeWindow);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? reason = null;
                var connected = false;
                try
                {
                    using var client = new TcpClient();
                    using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        connectTimeout.CancelAfter(_options.ConnectTimeout);
                        await client.ConnectAsync(_options.Host, _options.Port, connectTimeout.Token).ConfigureAwait(false);
                    }

                    var stream = client.GetStream();
                    var login = _options.Credentials is { UserName.Length: > 0 } cred ? LoginLine(cred) : "";
                    var hello = Encoding.UTF8.GetBytes(login + "listen 1\n");
                    await stream.WriteAsync(hello, ct).ConfigureAwait(false);

                    connected = true;
                    delay = _options.MinReconnectDelay;
                    output.TryWrite(new EventStreamStateEvent(true));

                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    var lines = 0;
                    while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                    {
                        lines++;
                        Handle(line, deduper, output);
                    }

                    if (login.Length > 0 && lines == 0)
                    {
                        // LMS closes the connection on a wrong login without a word. Do not try again and again.
                        _loginRejected = true;
                        output.TryWrite(new EventStreamStateEvent(false, "The server rejected the login."));
                        return;
                    }

                    reason = "The server closed the connection.";
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
                {
                    reason = ex.Message;
                }

                if (connected || reason is not null)
                {
                    output.TryWrite(new EventStreamStateEvent(false, reason));
                }

                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.MaxReconnectDelay.Ticks));
            }
        }
        catch (OperationCanceledException)
        {
            // Consumer stopped listening.
        }
        finally
        {
            output.TryComplete();
        }
    }

    /// <summary>Decodes, de-duplicates and parses one raw line; writes the event if there is one.</summary>
    internal static void Handle(string line, LineDeduper deduper, ChannelWriter<LmsEvent> output)
    {
        var words = LmsEventParser.Tokenize(line);
        if (words.Length == 0 || deduper.IsDuplicate(string.Join(' ', words), DateTimeOffset.UtcNow))
        {
            return;
        }

        if (LmsEventParser.Parse(words) is { } e)
        {
            output.TryWrite(e);
        }
    }
}
