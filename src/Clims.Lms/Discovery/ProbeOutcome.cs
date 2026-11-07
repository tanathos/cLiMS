using System.Net;

namespace Clims.Lms;

/// <summary>What a connection check found.</summary>
public enum ProbeOutcome
{
    /// <summary>The server answered and accepted the credentials (if any).</summary>
    Connected,

    /// <summary>The server wants a username and password, or rejected the ones given.</summary>
    LoginRequired,

    /// <summary>Nothing answered at that address (refused, unknown host, timeout).</summary>
    Unreachable,

    /// <summary>Something answered, but it is not a Lyrion Music Server.</summary>
    NotLms,
}
