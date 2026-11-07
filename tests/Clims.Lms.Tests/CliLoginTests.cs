using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Clims.Lms.Tests;

public class CliLoginTests
{
    [Fact]
    public void The_login_line_is_percent_encoded()
    {
        Assert.Equal("login me%20x p%26w%25\n", CliEventStream.LoginLine(new NetworkCredential("me x", "p&w%")));
    }

    [Fact]
    public async Task The_login_is_sent_before_listen()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var reader = new StreamReader(client.GetStream());
                var first = await reader.ReadLineAsync();
                var second = await reader.ReadLineAsync();
                await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes("login me ******\n"));
                return (first, second);
            });
            var stream = new CliEventStream(new CliEventStreamOptions
            {
                Host = "127.0.0.1",
                Port = port,
                Credentials = new NetworkCredential("me", "pw"),
            });
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await foreach (var e in stream.ReadAllAsync(cts.Token))
            {
                if (e is EventStreamStateEvent { Connected: true })
                {
                    break;
                }
            }

            Assert.Equal(("login me pw", "listen 1"), await server);
            Assert.False(stream.LoginRejected);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task A_dropped_login_stops_the_stream_instead_of_retrying()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accepted = 0;
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    Interlocked.Increment(ref accepted);
                    await new StreamReader(client.GetStream()).ReadLineAsync();
                } // closing right after the login line: what LMS does on a wrong password
            });
            var stream = new CliEventStream(new CliEventStreamOptions
            {
                Host = "127.0.0.1",
                Port = port,
                MinReconnectDelay = TimeSpan.FromMilliseconds(50),
                Credentials = new NetworkCredential("me", "bad"),
            });
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var events = await stream.ReadAllAsync(cts.Token).ToListAsync(cts.Token); // ends by itself
            Assert.True(stream.LoginRejected);
            Assert.Contains(events, e => e is EventStreamStateEvent { Connected: false, Reason: { } r } && r.Contains("login"));
            Assert.Equal(1, accepted);
        }
        finally
        {
            listener.Stop();
        }
    }
}
