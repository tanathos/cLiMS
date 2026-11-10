using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clims.Lyrics;

/// <summary>
/// LRCLIB (lrclib.net): a free, open lyrics database with timestamps, no key needed. The exact lookup uses artist, title,
/// album and duration; when that finds nothing, a search is filtered by artist and length so another version is not taken.
/// </summary>
public sealed partial class LrclibProvider : ILyricsProvider
{
    private const string Base = "https://lrclib.net/api/";
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(3);
    private readonly HttpClient _http;

    /// <summary>Creates the provider over a client (which must not carry credentials meant for another server).</summary>
    /// <param name="http">Client to use.</param>
    public LrclibProvider(HttpClient http)
    {
        _http = http;
    }

    /// <inheritdoc />
    public string Name => "lrclib";

    [GeneratedRegex(@"^\s*\[\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?\]", RegexOptions.Multiline)]
    private static partial Regex LineStamp();

    /// <inheritdoc />
    public async Task<LyricsResult?> FindAsync(LyricsQuery query, CancellationToken ct)
    {
        var url = Base + "get?artist_name=" + Uri.EscapeDataString(query.Artist) + "&track_name=" + Uri.EscapeDataString(query.Title) +
            "&album_name=" + Uri.EscapeDataString(query.Album) +
            (query.Duration > TimeSpan.Zero ? "&duration=" + (int)Math.Round(query.Duration.TotalSeconds) : "");
        var body = await LyricsHttp.GetBytesAsync(_http, url, ct).ConfigureAwait(false);
        if (body is not null)
        {
            using var doc = JsonDocument.Parse(body);
            if (Read(doc.RootElement) is { } exact)
            {
                return exact;
            }
        }

        var search = Base + "search?track_name=" + Uri.EscapeDataString(query.Title) + "&artist_name=" + Uri.EscapeDataString(query.Artist);
        var list = await LyricsHttp.GetBytesAsync(_http, search, ct).ConfigureAwait(false);
        if (list is null)
        {
            return null;
        }

        using var results = JsonDocument.Parse(list);
        if (results.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var artist = LyricsKey.Fold(query.Artist);
        var title = LyricsKey.Fold(query.Title);
        LyricsResult? best = null;
        foreach (var item in results.RootElement.EnumerateArray())
        {
            if (LyricsKey.Fold(Text(item, "artistName")) != artist || LyricsKey.Fold(Text(item, "trackName")) != title)
            {
                continue;
            }

            if (query.Duration > TimeSpan.Zero && item.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number &&
                (TimeSpan.FromSeconds(d.GetDouble()) - query.Duration).Duration() > Tolerance)
            {
                continue;
            }

            if (Read(item) is { } candidate && (best is null || (candidate.IsSynced && !best.IsSynced)))
            {
                best = candidate;
            }
        }

        return best;
    }

    private LyricsResult? Read(JsonElement item)
    {
        if (item.TryGetProperty("instrumental", out var i) && i.ValueKind == JsonValueKind.True)
        {
            return new LyricsResult("", null, Name, Instrumental: true);
        }

        var plain = Text(item, "plainLyrics").Replace("\r\n", "\n").Trim();
        var synced = Text(item, "syncedLyrics").Replace("\r\n", "\n").Trim();
        if (plain.Length == 0 && synced.Length == 0)
        {
            return null;
        }

        if (plain.Length == 0)
        {
            plain = StripStamps(synced);
        }

        return new LyricsResult(plain, synced.Length > 0 ? synced : null, Name);
    }

    private static string StripStamps(string lrc)
    {
        var sb = new StringBuilder();
        foreach (var line in lrc.Split('\n'))
        {
            var m = LineStamp().Match(line);
            if (m.Success)
            {
                sb.Append(line[m.Length..].Trim()).Append('\n');
            }
        }

        return sb.ToString().Trim();
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
