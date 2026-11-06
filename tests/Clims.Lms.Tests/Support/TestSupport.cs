using System.Net;
using System.Text;
using System.Text.Json;

namespace Clims.Lms.Tests;

/// <summary>One request captured by <see cref="FakeHandler"/>, with the JSON-RPC body already taken apart.</summary>
public sealed class RecordedRequest
{
    public RecordedRequest(HttpRequestMessage message, string body)
    {
        Message = message;
        Body = body;
        using var doc = JsonDocument.Parse(body);
        var parameters = doc.RootElement.GetProperty("params");
        PlayerId = parameters[0].GetString() ?? string.Empty;
        Command = parameters[1].EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText()).ToArray();
        Id = doc.RootElement.GetProperty("id").GetInt32();
    }

    public HttpRequestMessage Message { get; }

    public string Body { get; }

    public string PlayerId { get; }

    public string[] Command { get; }

    public int Id { get; }

    public string Text => string.Join(' ', Command);
}

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

public static class Fixtures
{
    public static string Load(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".json"));
}

public static class TestClient
{
    public static readonly Uri Base = new("http://lms.test:9000/");

    public static LmsClientOptions Options(Action<LmsClientOptionsBuilder>? tweak = null)
    {
        var b = new LmsClientOptionsBuilder();
        tweak?.Invoke(b);
        return new LmsClientOptions
        {
            BaseUri = Base,
            Credentials = b.Credentials,
            Timeout = b.Timeout,
            MaxConcurrency = b.MaxConcurrency,
            ReadRetries = b.ReadRetries,
            RetryDelay = TimeSpan.FromMilliseconds(1),
            MaxIdsPerRequest = b.MaxIdsPerRequest,
        };
    }

    public static LmsClient Create(FakeHandler handler, Action<LmsClientOptionsBuilder>? tweak = null) =>
        new(Options(tweak), new HttpClient(handler));

    /// <summary>A client that answers every request with the named fixture.</summary>
    public static (LmsClient Client, FakeHandler Handler) FromFixture(string name)
    {
        var handler = new FakeHandler(_ => FakeHandler.Json(Fixtures.Load(name)));
        return (Create(handler), handler);
    }

    /// <summary>A client that answers every request with an empty-ish success, for commands whose reply is ignored.</summary>
    public static (LmsClient Client, FakeHandler Handler) Silent()
    {
        var handler = new FakeHandler(_ => FakeHandler.Result("{\"count\":1}"));
        return (Create(handler), handler);
    }
}

public sealed class LmsClientOptionsBuilder
{
    public System.Net.NetworkCredential? Credentials { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    public int MaxConcurrency { get; set; } = 3;

    public int ReadRetries { get; set; } = 2;

    public int MaxIdsPerRequest { get; set; } = 400;
}

/// <summary>Runs only when <c>CLIMS_LMS_URL</c> points at a real server. Read-only tests only.</summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Integration.Url))
        {
            Skip = "Set CLIMS_LMS_URL (for example http://192.168.1.20:9000/) to run read-only integration tests.";
        }
    }
}

/// <summary>
/// Changes player state, so it needs a second opt-in: <c>CLIMS_LMS_ALLOW_WRITE=1</c> and <c>CLIMS_LMS_PLAYER=&lt;id&gt;</c>.
/// Use <see cref="PlayerSnapshot"/> to put the player back.
/// </summary>
public sealed class MutatingFactAttribute : FactAttribute
{
    public MutatingFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Integration.Url)
            || Environment.GetEnvironmentVariable("CLIMS_LMS_ALLOW_WRITE") != "1"
            || string.IsNullOrWhiteSpace(Integration.PlayerId))
        {
            Skip = "Mutating test: set CLIMS_LMS_URL, CLIMS_LMS_PLAYER and CLIMS_LMS_ALLOW_WRITE=1 (it changes the real player and restores it).";
        }
    }
}

public static class Integration
{
    public static string? Url => Environment.GetEnvironmentVariable("CLIMS_LMS_URL");

    public static string? PlayerId => Environment.GetEnvironmentVariable("CLIMS_LMS_PLAYER");

    public static LmsClient CreateClient() =>
        new(new LmsClientOptions { BaseUri = new Uri(Url!.EndsWith('/') ? Url : Url + "/") });
}
