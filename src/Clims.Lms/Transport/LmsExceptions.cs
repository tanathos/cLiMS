namespace Clims.Lms;

/// <summary>Base class for failures talking to the Lyrion Music Server.</summary>
public class LmsException : Exception
{
    /// <summary>Creates the exception.</summary>
    public LmsException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// The server could not be reached or did not answer in time (refused, DNS, timeout, dropped mid-request while
/// connecting). Usually transient; reads are retried automatically.
/// </summary>
public sealed class LmsConnectionException : LmsException
{
    /// <summary>Creates the exception.</summary>
    public LmsConnectionException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

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

/// <summary>The server rejected the credentials (HTTP 401/403).</summary>
public sealed class LmsAuthenticationException : LmsException
{
    /// <summary>Creates the exception.</summary>
    public LmsAuthenticationException(string message)
        : base(message)
    {
    }
}
