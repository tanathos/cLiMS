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
