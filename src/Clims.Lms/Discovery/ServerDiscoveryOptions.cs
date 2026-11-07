using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace Clims.Lms;

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
