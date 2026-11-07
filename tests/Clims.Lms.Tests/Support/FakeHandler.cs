using System.Net;
using System.Text;
using System.Text.Json;

namespace Clims.Lms.Tests;

/// <summary>An <see cref="HttpMessageHandler"/> that records requests and answers from a function.</summary>
public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>> _respond;

    /// <summary>The responder sees the request's cancellation token, like a real handler honouring timeouts.</summary>
    public FakeHandler(Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    public FakeHandler(Func<RecordedRequest, Task<HttpResponseMessage>> respond)
        : this((r, _) => respond(r))
    {
    }

    public FakeHandler(Func<RecordedRequest, HttpResponseMessage> respond)
        : this((r, _) => Task.FromResult(respond(r)))
    {
    }

    public List<RecordedRequest> Requests { get; } = new();

    /// <summary>A handler whose every request fails with the given exception.</summary>
    public static FakeHandler Throwing(Exception exception) =>
        new((Func<RecordedRequest, HttpResponseMessage>)(_ => throw exception));

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>Wraps a result object the way LMS does.</summary>
    public static HttpResponseMessage Result(string resultJson) => Json("{\"id\":1,\"method\":\"slim.request\",\"result\":" + resultJson + "}");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(request, body);
        lock (Requests)
        {
            Requests.Add(recorded);
        }

        return await _respond(recorded, cancellationToken);
    }
}
