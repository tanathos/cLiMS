using System.Net;

namespace Clims.Lyrics.Tests;

/// <summary>An HTTP handler that answers from a function and remembers the addresses asked.</summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<Uri, (HttpStatusCode Status, string Body)> _answer;

    public StubHandler(Func<Uri, (HttpStatusCode Status, string Body)> answer)
    {
        _answer = answer;
    }

    public List<Uri> Requests { get; } = new();

    public List<string?> UserAgents { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        UserAgents.Add(request.Headers.UserAgent.ToString());
        var (status, body) = _answer(request.RequestUri!);
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
