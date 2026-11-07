using System.Net;
using Clims.Lms;

namespace Clims.Tui.Ui;

/// <summary>
/// State and logic of the connect screen, without any drawing: the servers found, the search state, and the connection
/// check with its plain-words outcome. The view only shows this and forwards clicks and keys.
/// </summary>
public sealed class ConnectModel
{
    /// <summary>Port used when the typed address has none.</summary>
    public const int DefaultPort = 9000;

    private readonly Func<CancellationToken, IAsyncEnumerable<DiscoveredServer>> _discover;
    private readonly Func<Uri, NetworkCredential?, CancellationToken, Task<ProbeResult>> _probe;
    private readonly List<DiscoveredServer> _servers = new();

    /// <summary>Creates the model with real discovery and connection check.</summary>
    public ConnectModel()
        : this(ct => new ServerDiscovery().DiscoverAsync(ct), (uri, c, ct) => ServerProbe.CheckAsync(uri, c, cancellationToken: ct))
    {
    }

    /// <summary>Creates the model with replaceable network parts (tests).</summary>
    public ConnectModel(
        Func<CancellationToken, IAsyncEnumerable<DiscoveredServer>> discover,
        Func<Uri, NetworkCredential?, CancellationToken, Task<ProbeResult>> probe)
    {
        _discover = discover;
        _probe = probe;
    }

    /// <summary>Raised (any thread) when anything shown changed.</summary>
    public event Action? Changed;

    /// <summary>Raised (any thread) when the first server answers, so the form can offer it.</summary>
    public event Action<DiscoveredServer>? FirstServerFound;

    /// <summary>Servers that answered so far.</summary>
    public IReadOnlyList<DiscoveredServer> Servers
    {
        get
        {
            lock (_servers)
            {
                return _servers.ToArray();
            }
        }
    }

    /// <summary>True while the broadcast is still listening.</summary>
    public bool Searching { get; private set; }

    /// <summary>True while a connection check runs.</summary>
    public bool Busy { get; private set; }

    /// <summary>The line under the form: why a connection failed, or null.</summary>
    public string? Message { get; private set; }

    /// <summary>True when <see cref="Message"/> is an error.</summary>
    public bool MessageIsError { get; private set; }

    /// <summary>The last outcome of a connection check, to move the cursor to the right field.</summary>
    public ProbeOutcome? LastOutcome { get; private set; }

    /// <summary>Line for the discovery section.</summary>
    public string SearchLine => Searching
        ? "Searching the network…"
        : Servers.Count > 0
            ? "Pick a server (↑ ↓, Enter) or type an address below."
            : "No server answered. This is normal across a VPN or another subnet. Type its address below.";

    /// <summary>Listens for servers until the search time is up. Errors end the search quietly (the form still works).</summary>
    public async Task DiscoverAsync(CancellationToken ct)
    {
        Searching = true;
        Changed?.Invoke();
        try
        {
            await foreach (var server in _discover(ct).ConfigureAwait(false))
            {
                bool first;
                lock (_servers)
                {
                    _servers.Add(server);
                    first = _servers.Count == 1;
                }

                Changed?.Invoke();
                if (first)
                {
                    FirstServerFound?.Invoke(server);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.Net.Sockets.SocketException)
        {
            // Cancelled, or no usable network: the manual form is still there.
        }
        finally
        {
            Searching = false;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Checks the typed address and login. Returns the result to continue with, or null (see <see cref="Message"/> for why).
    /// Nothing is saved here.
    /// </summary>
    public async Task<ConnectResult?> ConnectAsync(string address, string username, string password, CancellationToken ct)
    {
        if (Busy)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            Fail("Type the address of the server, for example 192.168.1.20 or lms.local:9000.", null);
            return null;
        }

        var uri = AppSettings.NormalizeUrl(address, DefaultPort);
        if (uri is null)
        {
            Fail("That does not look like an address. Use a host name or IP, optionally with :port.", null);
            return null;
        }

        Busy = true;
        Message = $"Connecting to {uri.Host}:{uri.Port}…";
        MessageIsError = false;
        LastOutcome = null;
        Changed?.Invoke();
        try
        {
            var user = username.Trim();
            NetworkCredential? credentials = user.Length > 0 ? new NetworkCredential(user, password) : null;
            var result = await _probe(uri, credentials, ct).ConfigureAwait(false);
            if (result.Outcome != ProbeOutcome.Connected)
            {
                Fail(result.Message, result.Outcome);
                return null;
            }

            Message = result.Message;
            return new ConnectResult(ConnectChoice.Connected, uri, credentials?.UserName, credentials?.Password, result.Uuid);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    private void Fail(string message, ProbeOutcome? outcome)
    {
        Message = message;
        MessageIsError = true;
        LastOutcome = outcome;
        Changed?.Invoke();
    }
}
