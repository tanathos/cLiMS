using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace Clims.Lms;

/// <summary>Settings for <see cref="CliEventStream"/>.</summary>
public sealed class CliEventStreamOptions
{
    /// <summary>Server host name or address.</summary>
    public required string Host { get; init; }

    /// <summary>CLI port; 9090 by default.</summary>
    public int Port { get; init; } = 9090;

    /// <summary>First reconnect delay; doubled after every failed attempt up to <see cref="MaxReconnectDelay"/>.</summary>
    public TimeSpan MinReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Longest wait between reconnect attempts.</summary>
    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Identical lines closer together than this are reported once.</summary>
    public TimeSpan DedupeWindow { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How long a connect attempt may take.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Login for a server with password protection; sent as <c>login user password</c> after every connect. Never logged.</summary>
    public NetworkCredential? Credentials { get; init; }
}
