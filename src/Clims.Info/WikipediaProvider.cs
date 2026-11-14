using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clims.Info;

/// <summary>
/// The English Wikipedia: the biography of an artist (the introduction of its article, which is free to reuse under CC BY-SA 4.0 and
/// is shown with that credit) and a link to the article; the same for an album. The page is found through the artist's Wikidata entity when an earlier
/// provider knows it (exact); otherwise by a search whose answer must be a musician's article, because a wrong biography is worse than
/// none. 
/// </summary>
public sealed partial class WikipediaProvider : IInfoProvider
{
    private const string Api = "https://en.wikipedia.org/w/api.php";

    private readonly HttpClient _http;
    private readonly Action<string>? _log;

    /// <summary>Creates the provider over a client (which must not carry credentials meant for another server).</summary>
    /// <param name="http">Client to use.</param>
    /// <param name="log">Receives a line about how an article was found or why none was taken (null: no log).</param>
    public WikipediaProvider(HttpClient http, Action<string>? log = null)
    {
        _http = http;
        _log = log;
    }

    /// <inheritdoc />
    public string Name => "wikipedia";

    [GeneratedRegex(@"\b(band|musician|singer|rapper|group|duo|trio|composer|songwriter|vocalist|guitarist|drummer|pianist|orchestra|ensemble|DJ|producer|album|record label)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MusicWords();

    [GeneratedRegex(@"\b(album|EP|mixtape|soundtrack|live|compilation|studio|record|single)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AlbumWords();

    [GeneratedRegex(@"\n{2,}|\r?\n")]
    private static partial Regex LineBreaks();

    /// <inheritdoc />
    public async Task<ArtistInfo?> GetArtistAsync(InfoQuery query, CancellationToken ct)
    {
        if (InfoText.IsVariousArtists(query.Artist))
        {
            return null;
        }

        string? title;
        var guessed = false;
        if (query.WikidataId is { } entity)
        {
            title = await TitleFromWikidataAsync(entity, ct).ConfigureAwait(false);
        }
        else
        {
            title = await SearchAsync(query.Artist, query.Artist, t => MusicWords().IsMatch(t), ct).ConfigureAwait(false);
            guessed = true;
        }

        if (title is null || await ReadPageAsync(title, ct).ConfigureAwait(false) is not { } page)
        {
            _log?.Invoke($"wikipedia artist \"{query.Artist}\": no article ({(query.WikidataId is null ? "search found no title match" : "the Wikidata entry has none")})");
            return null;
        }

        if (guessed && !MusicWords().IsMatch(page.Text.Length > 500 ? page.Text[..500] : page.Text))
        {
            _log?.Invoke($"wikipedia artist \"{query.Artist}\": \"{page.Title}\" rejected, it is not about a musician");
            return null;
        }

        _log?.Invoke($"wikipedia artist \"{query.Artist}\": article \"{page.Title}\" taken ({(guessed ? "found by search" : "from Wikidata " + query.WikidataId)})");
        return new ArtistInfo(
            Name,
            page.Title,
            page.Title,
            null,
            null,
            null,
            null,
            null,
            false,
            null,
            page.Text,
            [],
            [],
            [new InfoLink("wikipedia", "https://en.wikipedia.org/wiki/" + Uri.EscapeDataString(page.Title.Replace(' ', '_')))],
            [],
            query.WikidataId,
            Name);
    }

    /// <inheritdoc />
    public async Task<AlbumInfo?> GetAlbumAsync(InfoQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Album))
        {
            return null;
        }

        string? title;
        var guessed = false;
        if (query.AlbumWikidataId is { } entity)
        {
            title = await TitleFromWikidataAsync(entity, ct).ConfigureAwait(false);
        }
        else
        {
            var artist = InfoText.IsVariousArtists(query.Artist) ? "" : " " + query.Artist;
            title = await SearchAsync(query.Album + artist, query.Album, t => AlbumWords().IsMatch(t) || InfoText.Normalize(t).Contains(InfoText.Normalize(query.Artist), StringComparison.Ordinal), ct).ConfigureAwait(false);
            guessed = true;
        }

        if (title is null || await ReadPageAsync(title, ct).ConfigureAwait(false) is not { } page)
        {
            _log?.Invoke($"wikipedia album \"{query.Album}\": no article ({(query.AlbumWikidataId is null ? "search found no title match" : "the Wikidata entry has none")})");
            return null;
        }

        // A guessed page must be about a record, and name the artist in its introduction.
        if (guessed)
        {
            var start = page.Text.Length > 800 ? page.Text[..800] : page.Text;
            var named = Mentions(query.Artist, start);
            if (!AlbumWords().IsMatch(start) || (!InfoText.IsVariousArtists(query.Artist) && !named))
            {
                _log?.Invoke($"wikipedia album \"{query.Album}\": \"{title}\" rejected, it is not about a record by {query.Artist}");
                return null;
            }
        }

        _log?.Invoke($"wikipedia album \"{query.Album}\": article \"{page.Title}\" taken ({(guessed ? "found by search" : "from Wikidata " + query.AlbumWikidataId)})");
        return new AlbumInfo(
            Name,
            page.Title,
            page.Title,
            null,
            [],
            null,
            [],
            null,
            0,
            null,
            null,
            null,
            null,
            [new InfoLink("wikipedia", "https://en.wikipedia.org/wiki/" + Uri.EscapeDataString(page.Title.Replace(' ', '_')))],
            query.AlbumWikidataId,
            page.Text,
            Name);
    }

    private static bool Mentions(string artist, string text) => InfoText.Normalize(text).Contains(InfoText.Normalize(artist), StringComparison.Ordinal);

    /// <summary>The title of the English article of a Wikidata entity, or null when it has none.</summary>
    private async Task<string?> TitleFromWikidataAsync(string entity, CancellationToken ct)
    {
        var url = $"https://www.wikidata.org/w/api.php?action=wbgetentities&ids={Uri.EscapeDataString(entity)}&props=sitelinks&sitefilter=enwiki&format=json";
        using var doc = await GetAsync(url, ct).ConfigureAwait(false);
        try
        {
            return doc?.RootElement.GetProperty("entities").GetProperty(entity).GetProperty("sitelinks").GetProperty("enwiki").GetProperty("title").GetString();
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The title of the article whose name is <paramref name="wanted"/>: an exact title, or one whose qualifier (the part in brackets) passes <paramref name="qualifierOk"/>.</summary>
    private async Task<string?> SearchAsync(string term, string wanted, Func<string, bool> qualifierOk, CancellationToken ct)
    {
        var url = $"{Api}?action=query&list=search&srsearch={Uri.EscapeDataString(term)}&srnamespace=0&srlimit=5&format=json&formatversion=2";
        using var doc = await GetAsync(url, ct).ConfigureAwait(false);
        if (doc is null || !doc.RootElement.TryGetProperty("query", out var q) || !q.TryGetProperty("search", out var hits) || hits.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var target = InfoText.Normalize(wanted);
        foreach (var hit in hits.EnumerateArray())
        {
            if (!hit.TryGetProperty("title", out var t) || t.GetString() is not { } title)
            {
                continue;
            }

            var open = title.IndexOf(" (", StringComparison.Ordinal);
            var bare = open >= 0 ? title[..open] : title;
            if (InfoText.Normalize(bare) == target && (open < 0 || qualifierOk(title[open..])))
            {
                return title;
            }
        }

        return null;
    }

    /// <summary>The introduction of an article, or null when the page is missing or a disambiguation page.</summary>
    private async Task<(string Title, string Text)?> ReadPageAsync(string title, CancellationToken ct)
    {
        var url = $"{Api}?action=query&prop=extracts|pageprops&exintro=1&explaintext=1&redirects=1&ppprop=disambiguation&titles={Uri.EscapeDataString(title)}&format=json&formatversion=2";
        using var doc = await GetAsync(url, ct).ConfigureAwait(false);
        if (doc is null || !doc.RootElement.TryGetProperty("query", out var q) || !q.TryGetProperty("pages", out var pages)
            || pages.ValueKind != JsonValueKind.Array || pages.GetArrayLength() == 0)
        {
            return null;
        }

        var page = pages[0];
        if (page.TryGetProperty("missing", out _) || page.TryGetProperty("pageprops", out _)
            || !page.TryGetProperty("extract", out var e) || e.GetString() is not { } extract || string.IsNullOrWhiteSpace(extract))
        {
            return null;
        }

        var name = page.TryGetProperty("title", out var n) && n.GetString() is { Length: > 0 } resolved ? resolved : title;
        return (name, LineBreaks().Replace(extract.Trim(), "\n\n"));
    }

    /// <summary>The JSON a request returns, or null for a 404 or an answer that is not JSON.</summary>
    private async Task<JsonDocument?> GetAsync(string url, CancellationToken ct)
    {
        var body = await InfoHttp.GetAsync(_http, url, "application/json", ct).ConfigureAwait(false);
        try
        {
            return body is null ? null : JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
