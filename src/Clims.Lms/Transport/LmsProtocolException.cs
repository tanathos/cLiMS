namespace Clims.Lms;

/// <summary>
/// The server answered, but not with a usable JSON-RPC reply. LMS reports no errors in-band: an unknown command,
/// unknown player id or malformed body gives an empty reply or a closed connection, which surfaces as this exception.
/// </summary>
public sealed class LmsProtocolException : LmsException
{
    /// <summary>Creates the exception.</summary>
    public LmsProtocolException(string message, string? command = null, string? playerId = null, Exception? inner = null)
        : base(message, inner)
    {
        Command = command;
        PlayerId = playerId;
    }

    /// <summary>The command that was sent, for diagnostics.</summary>
    public string? Command { get; }

    /// <summary>The player id the command was addressed to, if any.</summary>
    public string? PlayerId { get; }
}
