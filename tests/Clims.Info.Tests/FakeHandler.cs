using System.Net;
using System.Text;

namespace Clims.Info.Tests;

/// <summary>Answers requests from a table of address fragments to bodies, and remembers what was asked.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly List<(string Fragment, string Body)> _routes = new();

    public List<string> Requests { get; } = new();

    public FakeHandler On(string fragment, string body)
    {
        _routes.Add((fragment, body));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
        Requests.Add(url);
        foreach (var (fragment, body) in _routes)
        {
            if (url.Contains(fragment, StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
            }
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
