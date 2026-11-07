using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Clims.Lms.Tests;

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
