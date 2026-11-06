namespace Clims.Lms;

/// <summary>
/// Default <see cref="ILmsClient"/>. Create one per server and share it: it limits concurrent requests, which
/// matters because LMS is mostly single-threaded.
/// </summary>
public sealed class LmsClient : ILmsClient, IDisposable
{
    private readonly HttpClient? _ownedHttp;

    /// <summary>Creates a client. Pass your own <see cref="HttpClient"/> (for example from <c>IHttpClientFactory</c>) or let it create one.</summary>
    public LmsClient(LmsClientOptions options, HttpClient? http = null)
    {
        _ownedHttp = http is null ? new HttpClient() : null;
        var rpc = new LmsRpcClient(http ?? _ownedHttp!, options);
        BaseUri = options.BaseUri;
        Rpc = rpc;
        Library = new LibraryApi(rpc);
        Player = new PlayerApi(rpc);
        Queue = new QueueApi(rpc, options);
        Server = new ServerApi(rpc);
    }

    /// <inheritdoc />
    public Uri BaseUri { get; }

    /// <inheritdoc />
    public ILmsRpcClient Rpc { get; }

    /// <inheritdoc />
    public ILibraryApi Library { get; }

    /// <inheritdoc />
    public IPlayerApi Player { get; }

    /// <inheritdoc />
    public IQueueApi Queue { get; }

    /// <inheritdoc />
    public IServerApi Server { get; }

    /// <summary>Disposes the <see cref="HttpClient"/> only if this instance created it.</summary>
    public void Dispose() => _ownedHttp?.Dispose();
}
