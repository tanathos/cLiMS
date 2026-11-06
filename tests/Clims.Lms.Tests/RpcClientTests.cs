using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Clims.Lms.Tests;

public class RpcClientTests
{
    private static ILmsRpcClient Rpc(FakeHandler handler, Action<LmsClientOptionsBuilder>? tweak = null) =>
        TestClient.Create(handler, tweak).Rpc;

    [Fact]
    public void Body_has_the_slim_request_shape_and_numbers_stay_numbers()
    {
        var body = Encoding.UTF8.GetString(LmsRpcClient.BuildBody(7, "b8:27:eb:a9:77:8f", new object[] { "albums", 0, 50L, "tags:ly", true, 2.5 }));
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal(7, root.GetProperty("id").GetInt32());
        Assert.Equal("slim.request", root.GetProperty("method").GetString());
        var p = root.GetProperty("params");
        Assert.Equal("b8:27:eb:a9:77:8f", p[0].GetString());
        var cmd = p[1];
        Assert.Equal(JsonValueKind.String, cmd[0].ValueKind);
        Assert.Equal(JsonValueKind.Number, cmd[1].ValueKind);
        Assert.Equal(JsonValueKind.Number, cmd[2].ValueKind);
        Assert.Equal("tags:ly", cmd[3].GetString());
        Assert.Equal("1", cmd[4].GetString());
        Assert.Equal(2.5, cmd[5].GetDouble());
    }

    [Fact]
    public void Server_wide_commands_use_an_empty_player_id()
    {
        using var doc = JsonDocument.Parse(LmsRpcClient.BuildBody(1, null, new object[] { "version", "?" }));
        Assert.Equal(string.Empty, doc.RootElement.GetProperty("params")[0].GetString());
    }

    [Fact]
    public void Special_characters_are_escaped_not_corrupted()
    {
        var body = Encoding.UTF8.GetString(LmsRpcClient.BuildBody(1, null, new object[] { "search", "term:Pantéon \"x\" & 日本" }));
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("term:Pantéon \"x\" & 日本", doc.RootElement.GetProperty("params")[1][1].GetString());
    }

    [Fact]
    public async Task Posts_to_jsonrpc_and_returns_the_result_element()
    {
        var handler = new FakeHandler(_ => FakeHandler.Result("{\"_version\":\"9.1.1\"}"));
        var result = await Rpc(handler).RequestAsync(null, new object[] { "version", "?" }, RequestKind.Read);

        Assert.Equal("9.1.1", result.GetProperty("_version").GetString());
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Message.Method);
        Assert.Equal("http://lms.test:9000/jsonrpc.js", request.Message.RequestUri!.ToString());
        Assert.Equal("application/json", request.Message.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Request_ids_increase()
    {
        var handler = new FakeHandler(_ => FakeHandler.Result("{}"));
        var rpc = Rpc(handler);
        await rpc.RequestAsync(null, new object[] { "a" });
        await rpc.RequestAsync(null, new object[] { "b" });
        Assert.Equal(2, handler.Requests.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public async Task Basic_credentials_are_sent_when_configured()
    {
        var handler = new FakeHandler(_ => FakeHandler.Result("{}"));
        var rpc = Rpc(handler, o => o.Credentials = new NetworkCredential("user", "pä:ss"));
        await rpc.RequestAsync(null, new object[] { "version", "?" });

        var auth = handler.Requests[0].Message.Headers.Authorization!;
        Assert.Equal("Basic", auth.Scheme);
        Assert.Equal("user:pä:ss", Encoding.UTF8.GetString(Convert.FromBase64String(auth.Parameter!)));
    }

    [Fact]
    public async Task No_credentials_means_no_authorization_header()
    {
        var handler = new FakeHandler(_ => FakeHandler.Result("{}"));
        await Rpc(handler).RequestAsync(null, new object[] { "version", "?" });
        Assert.Null(handler.Requests[0].Message.Headers.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Rejected_credentials_raise_an_authentication_exception(HttpStatusCode status)
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("", status));
        await Assert.ThrowsAsync<LmsAuthenticationException>(() =>
            Rpc(handler).RequestAsync(null, new object[] { "version", "?" }, RequestKind.Read));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task An_empty_reply_is_a_protocol_error_that_names_the_command_and_player()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json(""));
        var ex = await Assert.ThrowsAsync<LmsProtocolException>(() =>
            Rpc(handler).RequestAsync("aa:bb:cc:dd:ee:ff", new object[] { "nosuchcommand", 1 }, RequestKind.Read));

        Assert.Equal("nosuchcommand 1", ex.Command);
        Assert.Equal("aa:bb:cc:dd:ee:ff", ex.PlayerId);
        Assert.Contains("nosuchcommand", ex.Message);
        Assert.Single(handler.Requests); // not retried: asking again will not help
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"id\":1}")]
    public async Task A_reply_without_a_result_is_a_protocol_error(string body)
    {
        var handler = new FakeHandler(_ => FakeHandler.Json(body));
        await Assert.ThrowsAsync<LmsProtocolException>(() => Rpc(handler).RequestAsync(null, new object[] { "x" }));
    }

    [Fact]
    public async Task A_result_that_is_an_empty_object_is_valid()
    {
        // 'playlists' with none saved returns exactly this.
        var handler = new FakeHandler(_ => FakeHandler.Json(Fixtures.Load("playlists")));
        var result = await Rpc(handler).RequestAsync(null, new object[] { "playlists", 0, 5 }, RequestKind.Read);
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
    }

    [Fact]
    public async Task Other_http_failures_are_protocol_errors()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("oops", HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<LmsProtocolException>(() => Rpc(handler).RequestAsync(null, new object[] { "x" }));
    }

    [Fact]
    public async Task Reads_are_retried_after_connection_failures()
    {
        var attempts = 0;
        var handler = new FakeHandler(_ =>
        {
            if (++attempts < 3)
            {
                throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");
            }

            return FakeHandler.Result("{\"ok\":1}");
        });

        var result = await Rpc(handler).RequestAsync(null, new object[] { "version", "?" }, RequestKind.Read);
        Assert.Equal(3, attempts);
        Assert.Equal(1, result.GetProperty("ok").GetInt32());
    }

    [Fact]
    public async Task Reads_give_up_after_the_configured_retries()
    {
        var handler = FakeHandler.Throwing(new HttpRequestException(HttpRequestError.ConnectionError, "refused"));
        await Assert.ThrowsAsync<LmsConnectionException>(() => Rpc(handler, o => o.ReadRetries = 1).RequestAsync(null, new object[] { "x" }, RequestKind.Read));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Commands_are_never_retried()
    {
        // Retrying "playlist index +1" after an ambiguous failure could skip two tracks.
        var handler = FakeHandler.Throwing(new HttpRequestException(HttpRequestError.ConnectionError, "refused"));
        await Assert.ThrowsAsync<LmsConnectionException>(() => Rpc(handler).RequestAsync("p", new object[] { "playlist", "index", "+1" }, RequestKind.Command));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_closed_connection_without_a_reply_is_a_protocol_error_and_is_not_retried()
    {
        var handler = FakeHandler.Throwing(new HttpRequestException(HttpRequestError.ResponseEnded, "ended"));
        await Assert.ThrowsAsync<LmsProtocolException>(() => Rpc(handler).RequestAsync(null, new object[] { "x" }, RequestKind.Read));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_timeout_is_a_connection_error()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return FakeHandler.Result("{}");
        });

        var ex = await Assert.ThrowsAsync<LmsConnectionException>(() =>
            Rpc(handler, o => { o.Timeout = TimeSpan.FromMilliseconds(50); o.ReadRetries = 0; }).RequestAsync(null, new object[] { "x" }, RequestKind.Read));
        Assert.Contains("did not answer", ex.Message);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_turned_into_a_connection_error()
    {
        using var cts = new CancellationTokenSource();
        var handler = new FakeHandler(async r =>
        {
            cts.Cancel();
            await Task.Delay(TimeSpan.FromSeconds(30), cts.Token);
            return FakeHandler.Result("{}");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Rpc(handler).RequestAsync(null, new object[] { "x" }, RequestKind.Read, cts.Token));
    }

    [Fact]
    public async Task Concurrent_requests_are_limited()
    {
        var active = 0;
        var peak = 0;
        var handler = new FakeHandler(async _ =>
        {
            var now = Interlocked.Increment(ref active);
            int seen;
            while (now > (seen = Volatile.Read(ref peak)) && Interlocked.CompareExchange(ref peak, now, seen) != seen)
            {
            }

            await Task.Delay(30);
            Interlocked.Decrement(ref active);
            return FakeHandler.Result("{}");
        });

        var rpc = Rpc(handler, o => o.MaxConcurrency = 2);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => rpc.RequestAsync(null, new object[] { "x", i }, RequestKind.Read)));

        Assert.Equal(12, handler.Requests.Count);
        Assert.InRange(peak, 1, 2);
    }

    [Fact]
    public async Task Nothing_listening_is_a_connection_error()
    {
        // Real socket: bind a port, release it, and connect to it.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        using var client = new LmsClient(new LmsClientOptions { BaseUri = new Uri($"http://127.0.0.1:{port}/"), ReadRetries = 0, Timeout = TimeSpan.FromSeconds(5) });
        await Assert.ThrowsAsync<LmsConnectionException>(() => client.Server.GetVersionAsync());
    }

    [Fact]
    public async Task A_server_that_accepts_and_hangs_up_is_a_protocol_error()
    {
        // This is exactly how LMS answers an unknown command: the socket closes without an HTTP reply.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync();
            var buffer = new byte[4096];
            await socket.ReceiveAsync(buffer);
            socket.Close();
        });

        try
        {
            using var client = new LmsClient(new LmsClientOptions { BaseUri = new Uri($"http://127.0.0.1:{port}/"), ReadRetries = 0, Timeout = TimeSpan.FromSeconds(5) });
            await Assert.ThrowsAsync<LmsProtocolException>(() => client.Rpc.RequestAsync(null, new object[] { "bogus" }, RequestKind.Read));
        }
        finally
        {
            listener.Stop();
            await server;
        }
    }
}
