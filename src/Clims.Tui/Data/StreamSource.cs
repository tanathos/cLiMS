namespace Clims.Tui.Data;

/// <summary>Which service a stream comes from, told by the address scheme the server's plugin gives it (<c>spotify://track:…</c>, <c>tidal://…</c>).</summary>
public static class StreamSource
{
    private static readonly string[] Plain = ["http", "https", "file", "mms", "rtmp", "rtsp", "mmsh", "icy"];

    /// <summary>The name to show ("Spotify", "TIDAL"…), or null for a library file or an ordinary web radio address.</summary>
    public static string? NameOf(string? url)
    {
        var colon = url?.IndexOf(':') ?? -1;
        if (colon <= 1 || url is null)
        {
            return null;
        }

        var scheme = url[..colon].ToLowerInvariant();
        if (Plain.Contains(scheme))
        {
            return null;
        }

        return scheme switch
        {
            "spotify" => "Spotify",
            "spoton" => "SpotOn",
            "spotty" => "Spotty",
            "tidal" => "TIDAL",
            "qobuz" => "Qobuz",
            "deezer" => "Deezer",
            "youtube" => "YouTube",
            _ => char.ToUpperInvariant(scheme[0]) + scheme[1..],
        };
    }
}
