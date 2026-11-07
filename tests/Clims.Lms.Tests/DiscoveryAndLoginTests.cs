using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Clims.Lms.Tests;

public class ServerDiscoveryTests
{
    private static byte[] Reply(params (string Tag, string Value)[] tags)
    {
        var bytes = new List<byte> { (byte)'E' };
        foreach (var (tag, value) in tags)
        {
            var v = Encoding.UTF8.GetBytes(value);
            bytes.AddRange(Encoding.ASCII.GetBytes(tag));
            bytes.Add((byte)v.Length);
            bytes.AddRange(v);
        }

        return bytes.ToArray();
    }

    [Fact]
    public void The_request_asks_for_the_five_tags()
    {
        var request = ServerDiscovery.BuildRequest();
        Assert.Equal((byte)'e', request[0]);
        Assert.Equal("NAME\0JSON\0VERS\0UUID\0CLIP\0", Encoding.ASCII.GetString(request, 1, request.Length - 1));
    }

    [Fact]
    public void A_reply_is_read_into_a_server()
    {
        var server = ServerDiscovery.ParseReply(
            Reply(("NAME", "Mnemonic Music"), ("JSON", "9000"), ("VERS", "9.1.1"), ("UUID", "abc"), ("CLIP", "9090")),
            "192.168.1.20")!;
        Assert.Equal("Mnemonic Music", server.Name);
        Assert.Equal(new Uri("http://192.168.1.20:9000/"), server.BaseUri);
        Assert.Equal("9.1.1", server.Version);
        Assert.Equal(9090, server.CliPort);
    }

    [Fact]
    public void Garbage_and_requests_are_not_servers()
    {
        Assert.Null(ServerDiscovery.ParseReply(Encoding.ASCII.GetBytes("e..."), "1.2.3.4"));
        Assert.Null(ServerDiscovery.ParseReply(ReadOnlySpan<byte>.Empty, "1.2.3.4"));
        Assert.Null(ServerDiscovery.ParseReply(Reply(("XXXX", "y")), "1.2.3.4"));
    }

    [Fact]
    public void A_truncated_reply_does_not_throw()
    {
        var bytes = Reply(("NAME", "Mnemonic Music"), ("JSON", "9000"));
        Assert.NotNull(ServerDiscovery.ParseReply(bytes.AsSpan(0, bytes.Length - 2), "h"));
    }

    [Fact]
    public async Task Servers_that_answer_over_a_real_socket_are_found_once_each()
    {
        using var fake = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)fake.Client.LocalEndPoint!).Port;
        var responder = Task.Run(async () =>
        {
            var received = await fake.ReceiveAsync();
            Assert.Equal((byte)'e', received.Buffer[0]);
            var reply = Reply(("NAME", "Test LMS"), ("JSON", "9001"), ("UUID", "u1"));
            await fake.SendAsync(reply, received.RemoteEndPoint);
            await fake.SendAsync(reply, received.RemoteEndPoint); // the same server heard twice
        });

        var found = new List<DiscoveredServer>();
        var discovery = new ServerDiscovery(new ServerDiscoveryOptions
        {
            Port = port,
            Duration = TimeSpan.FromMilliseconds(600),
            Targets = [IPAddress.Loopback],
        });
        await foreach (var s in discovery.DiscoverAsync())
        {
            found.Add(s);
        }

        await responder;
        var one = Assert.Single(found);
        Assert.Equal("Test LMS", one.Name);
        Assert.Equal(9001, one.WebPort);
    }

    [Fact]
    public async Task Nothing_answering_ends_the_search_empty()
    {
        var discovery = new ServerDiscovery(new ServerDiscoveryOptions
        {
            Port = 1,
            Duration = TimeSpan.FromMilliseconds(300),
            Targets = [IPAddress.Loopback],
        });
        Assert.Empty(await discovery.DiscoverAsync().ToListAsync());
    }

    [Fact]
    public void Every_interface_broadcast_is_offered_besides_the_limited_one()
    {
        var all = ServerDiscovery.BroadcastAddresses();
        Assert.Equal(IPAddress.Broadcast, all[0]);
    }
}

public class ServerProbeTests
{
    private static readonly Uri Server = new("http://lms.test:9000/");

    [Fact]
    public async Task A_reachable_server_is_connected_and_credentials_are_sent()
    {
        var handler = new FakeHandler(_ => FakeHandler.Result("{\"version\":\"9.1.1\",\"uuid\":\"u\",\"player count\":0}"));
        using var http = new HttpClient(handler);
        var result = await ServerProbe.CheckAsync(Server, new NetworkCredential("me", "pw"), http);
        Assert.Equal(ProbeOutcome.Connected, result.Outcome);
        Assert.Equal("u", result.Uuid);
        Assert.Equal("Basic", handler.Requests[0].Message.Headers.Authorization?.Scheme);
    }

    [Theory]
    [InlineData(false, "asks for a username")]
    [InlineData(true, "Wrong username")]
    public async Task A_401_is_a_login_problem_in_plain_words(bool withLogin, string expected)
    {
        using var http = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var result = await ServerProbe.CheckAsync(Server, withLogin ? new NetworkCredential("me", "bad") : null, http);
        Assert.Equal(ProbeOutcome.LoginRequired, result.Outcome);
        Assert.Contains(expected, result.Message);
    }

    [Fact]
    public async Task Nothing_listening_is_unreachable()
    {
        using var http = new HttpClient(FakeHandler.Throwing(new HttpRequestException("refused")));
        Assert.Equal(ProbeOutcome.Unreachable, (await ServerProbe.CheckAsync(Server, null, http)).Outcome);
    }

    [Fact]
    public async Task A_web_server_that_is_not_lms_is_reported_as_such()
    {
        using var http = new HttpClient(new FakeHandler(_ => FakeHandler.Json("<html>hello</html>")));
        Assert.Equal(ProbeOutcome.NotLms, (await ServerProbe.CheckAsync(Server, null, http)).Outcome);
    }
}

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
