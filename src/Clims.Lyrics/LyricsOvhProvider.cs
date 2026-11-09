using System.Text.Json;

namespace Clims.Lyrics;

/// <summary>lyrics.ovh: one JSON endpoint by artist and title, no key, plain text only. Less careful than LRCLIB, so it is a last resort.</summary>
public sealed class LyricsOvhProvider : ILyricsProvider
{
    private readonly HttpClient _http;

    /// <summary>Creates the provider over a client (which must not carry credentials meant for another server).</summary>
    /// <param name="http">Client to use.</param>
    public LyricsOvhProvider(HttpClient http)
    {
        _http = http;
    }

    /// <inheritdoc />
    public string Name => "lyricsovh";

    /// <inheritdoc />
    public async Task<LyricsResult?> FindAsync(LyricsQuery query, CancellationToken ct)
    {
        var url = "https://api.lyrics.ovh/v1/" + Uri.EscapeDataString(query.Artist) + "/" + Uri.EscapeDataString(query.Title);
        var body = await LyricsHttp.GetBytesAsync(_http, url, ct).ConfigureAwait(false);
        if (body is null)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("lyrics", out var lyrics) || lyrics.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var lines = (lyrics.GetString() ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        // The site prefixes some entries with its own heading.
        if (lines.Count > 0 && lines[0].StartsWith("Paroles de la chanson", StringComparison.OrdinalIgnoreCase))
        {
            lines.RemoveAt(0);
        }

        var text = string.Join('\n', lines).Trim();
        return text.Length == 0 ? null : new LyricsResult(text, null, Name);
    }
}
