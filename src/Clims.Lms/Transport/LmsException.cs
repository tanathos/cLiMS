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
