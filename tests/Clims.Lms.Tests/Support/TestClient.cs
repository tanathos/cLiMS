using System.Net;
using System.Text;
using System.Text.Json;

namespace Clims.Lms.Tests;

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
