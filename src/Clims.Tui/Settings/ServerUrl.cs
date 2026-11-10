namespace Clims.Tui.Settings;

/// <summary>Server addresses as people type them.</summary>
public static class ServerUrl
{
    /// <summary>Accepts <c>host</c>, <c>host:9000</c> or a full URL, and returns an http URI ending in a slash; null if unusable.</summary>
    /// <param name="text">What the user typed.</param>
    /// <param name="defaultPort">Port to add when the text is a bare host without scheme or port (null: leave it to http's 80).</param>
    public static Uri? Normalize(string? text, int? defaultPort = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var t = text.Trim();
        if (!t.Contains("://", StringComparison.Ordinal))
        {
            t = "http://" + t;
        }

        if (!t.EndsWith('/'))
        {
            t += "/";
        }

        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        if (defaultPort is { } port && !text.Contains("://", StringComparison.Ordinal) && uri.IsDefaultPort)
        {
            return new UriBuilder(uri) { Port = port }.Uri;
        }

        return uri;
    }
}
