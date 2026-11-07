namespace Clims.Lms;

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
