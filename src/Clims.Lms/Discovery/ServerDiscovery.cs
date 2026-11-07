using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace Clims.Lms;

/// <summary>A Lyrion Music Server that answered the discovery broadcast.</summary>
/// <param name="Name">Server name (the NAME tag).</param>
/// <param name="Host">Address the reply came from.</param>
/// <param name="WebPort">Web and JSON-RPC port (the JSON tag), 9000 by default.</param>
/// <param name="Version">Server version (VERS), when sent.</param>
/// <param name="Uuid">Server UUID (UUID), when sent.</param>
/// <param name="CliPort">Command line port (CLIP), when sent.</param>
public sealed record DiscoveredServer(string Name, string Host, int WebPort, string? Version, string? Uuid, int? CliPort)
{
    /// <summary>The address to connect to, for example <c>http://192.168.1.20:9000/</c>.</summary>
    public Uri BaseUri => new($"http://{(Host.Contains(':') ? $"[{Host}]" : Host)}:{WebPort}/");

    /// <summary>Same server, same address: discovery can hear one server on several routes.</summary>
    public string Key => Uuid ?? $"{Host}:{WebPort}";
}

/// <summary>Settings for <see cref="ServerDiscovery"/>.</summary>
public sealed class ServerDiscoveryOptions
{
    /// <summary>UDP port servers listen on; 3483.</summary>
    public int Port { get; init; } = 3483;

    /// <summary>How long to listen for answers.</summary>
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Where to send the request; null: the limited broadcast plus the broadcast address of every network interface.</summary>
    public IReadOnlyList<IPAddress>? Targets { get; init; }
}

/// <summary>
/// Finds servers on the local network the way Squeezebox players do: a UDP broadcast to port 3483 asking for NAME, JSON,
/// VERS, UUID and CLIP; every server answers with those tags. Read-only. It does not cross routers or VPNs.
/// </summary>
public sealed class ServerDiscovery
{
    private static readonly string[] Tags = ["NAME", "JSON", "VERS", "UUID", "CLIP"];
    private readonly ServerDiscoveryOptions _options;

    /// <summary>Creates the discovery.</summary>
    public ServerDiscovery(ServerDiscoveryOptions? options = null)
    {
        _options = options ?? new ServerDiscoveryOptions();
    }

    /// <summary>The request datagram: <c>e</c> then each tag followed by a zero length byte.</summary>
    public static byte[] BuildRequest()
    {
        var bytes = new List<byte> { (byte)'e' };
        foreach (var tag in Tags)
        {
            bytes.AddRange(Encoding.ASCII.GetBytes(tag));
            bytes.Add(0);
        }

        return bytes.ToArray();
    }

    /// <summary>Reads a reply (<c>E</c> then tag, length byte, value ...); null when it is not a server answer.</summary>
    public static DiscoveredServer? ParseReply(ReadOnlySpan<byte> data, string host)
    {
        if (data.Length < 1 || data[0] != (byte)'E')
        {
            return null;
        }

        var values = new Dictionary<string, string>();
        var i = 1;
        while (i + 5 <= data.Length)
        {
            var tag = Encoding.ASCII.GetString(data.Slice(i, 4));
            var length = data[i + 4];
            i += 5;
            if (i + length > data.Length)
            {
                break;
            }

            values[tag] = Encoding.UTF8.GetString(data.Slice(i, length));
            i += length;
        }

        if (!values.TryGetValue("JSON", out var json) || !int.TryParse(json, out var web) || web is < 1 or > 65535)
        {
            // A server that did not say its web port is assumed to use the default one.
            web = values.ContainsKey("NAME") ? 9000 : 0;
        }

        if (web == 0)
        {
            return null;
        }

        return new DiscoveredServer(
            values.GetValueOrDefault("NAME") is { Length: > 0 } name ? name : host,
            host,
            web,
            values.GetValueOrDefault("VERS"),
            values.GetValueOrDefault("UUID"),
            values.TryGetValue("CLIP", out var clip) && int.TryParse(clip, out var cli) ? cli : null);
    }

    /// <summary>Sends the request and yields each server as it answers, once per server, until the time is up.</summary>
    public async IAsyncEnumerable<DiscoveredServer> DiscoverAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        var request = BuildRequest();
        foreach (var target in _options.Targets ?? BroadcastAddresses())
        {
            try
            {
                await socket.SendAsync(request, new IPEndPoint(target, _options.Port), cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                // An adapter that cannot broadcast (disconnected, VPN): the others may still work.
            }
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(_options.Duration);
        var seen = new HashSet<string>();
        while (true)
        {
            UdpReceiveResult reply;
            try
            {
                reply = await socket.ReceiveAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (SocketException)
            {
                continue; // an ICMP "port unreachable" from one target must not end the search
            }

            if (ParseReply(reply.Buffer, reply.RemoteEndPoint.Address.ToString()) is { } server && seen.Add(server.Key))
            {
                yield return server;
            }
        }
    }

    /// <summary>The limited broadcast plus every interface's directed broadcast (a PC with several adapters may route the first one wrongly).</summary>
    public static IReadOnlyList<IPAddress> BroadcastAddresses()
    {
        var list = new List<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var ip in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily != AddressFamily.InterNetwork || ip.IPv4Mask is null)
                    {
                        continue;
                    }

                    var a = ip.Address.GetAddressBytes();
                    var m = ip.IPv4Mask.GetAddressBytes();
                    var b = new IPAddress(a.Select((x, k) => (byte)(x | ~m[k])).ToArray());
                    if (!list.Contains(b))
                    {
                        list.Add(b);
                    }
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Fall back to the limited broadcast alone.
        }

        return list;
    }
}
