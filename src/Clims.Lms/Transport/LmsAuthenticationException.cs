namespace Clims.Lms;

/// <summary>The server rejected the credentials (HTTP 401/403).</summary>
public sealed class LmsAuthenticationException : LmsException
{
    /// <summary>Creates the exception.</summary>
    public LmsAuthenticationException(string message)
        : base(message)
    {
    }
}
