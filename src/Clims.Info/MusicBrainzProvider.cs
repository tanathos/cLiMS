using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clims.Info;

/// <summary>
/// MusicBrainz (musicbrainz.org): the open music encyclopedia. No key is needed, but the site allows one request per second, so every
/// request waits its turn. It gives the type, origin, years, genres, members and web links of an artist, and the type, date, label and
/// rating of an album. It has no biography and no pictures of its own; where an artist's page links a picture on Wikimedia Commons,
/// that one is offered.
/// </summary>
public sealed partial class MusicBrainzProvider : IInfoProvider
{
    private const string Base = "https://musicbrainz.org/ws/2/";
    private const double MinScore = 80;
    private const int MaxGenres = 8;

    private readonly HttpClient _http;
    private readonly TimeSpan _gap;
    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _turn = new(1, 1);
    private DateTime _lastRequest = DateTime.MinValue;

    /// <summary>Creates the provider over a client (which must not carry credentials meant for another server).</summary>
    /// <param name="http">Client to use.</param>
    /// <param name="minInterval">Time between two requests; MusicBrainz asks for a second (tests pass zero).</param>
    /// <param name="log">Receives a line about each search: how many candidates, which one was taken (null: no log).</param>
    public MusicBrainzProvider(HttpClient http, TimeSpan? minInterval = null, Action<string>? log = null)
    {
        _http = http;
        _gap = minInterval ?? TimeSpan.FromMilliseconds(1100);
        _log = log;
    }

    /// <inheritdoc />
    public string Name => "musicbrainz";

    [GeneratedRegex(@"\[\[(?:[^\]|]*\|)?([^\]]*)\]\]|\[(?:https?://\S+)\s+([^\]]*)\]|'{2,}")]
    private static partial Regex WikiMarkup();

    /// <inheritdoc />
    public async Task<ArtistInfo?> GetArtistAsync(InfoQuery query, CancellationToken ct)
    {
        var id = query.ArtistId;
        if (id is null)
        {
            if (InfoText.IsVariousArtists(query.Artist))
            {
                return null;
            }

            id = await FindAsync("artist", "artists", $"artist:\"{InfoText.Lucene(query.Artist)}\"", query.Artist, "name", ct).ConfigureAwait(false);
            if (id is null)
            {
                return null;
            }
        }

        using var doc = await GetAsync($"artist/{id}?inc=genres+tags+url-rels+artist-rels+annotation", ct).ConfigureAwait(false);
        if (doc is null)
        {
            return null;
        }

        var artist = ReadArtist(doc.RootElement);
        if (artist.ImageUrls.Count == 0 && artist.Links.FirstOrDefault(l => l.Kind == "wikidata") is { } wikidata && WikidataId(wikidata.Url) is { } entity)
        {
            // MusicBrainz rarely links a picture itself, but the artist's Wikidata entry usually names one.
            if (await WikidataImageAsync(entity, ct).ConfigureAwait(false) is { } image)
            {
                artist = artist with { ImageUrls = [image] };
            }
        }

        return artist;
    }

    /// <inheritdoc />
    public async Task<AlbumInfo?> GetAlbumAsync(InfoQuery query, CancellationToken ct)
    {
        var id = query.AlbumId;
        if (id is null)
        {
            if (string.IsNullOrWhiteSpace(query.Album))
            {
                return null;
            }

            var who = query.ArtistId is not null ? $" AND arid:{query.ArtistId}" : InfoText.IsVariousArtists(query.Artist) ? "" : $" AND artist:\"{InfoText.Lucene(query.Artist)}\"";
            id = await FindAsync("release-group", "release-groups", $"releasegroup:\"{InfoText.Lucene(query.Album)}\"{who}", query.Album, "title", ct).ConfigureAwait(false);
            if (id is null)
            {
                return null;
            }
        }

        using var doc = await GetAsync($"release-group/{id}?inc=genres+tags+ratings+releases+url-rels", ct).ConfigureAwait(false);
        if (doc is null)
        {
            return null;
        }

        var root = doc.RootElement;
        var (release, date) = FirstRelease(root);
        (string? Label, string? Catalog, string? Country, int? Tracks) details = default;
        if (release is not null)
        {
            try
            {
                using var detail = await GetAsync($"release/{release}?inc=labels+recordings", ct).ConfigureAwait(false);
                if (detail is not null)
                {
                    details = ReadRelease(detail.RootElement);
                }
            }
            catch (HttpRequestException)
            {
                // The release is a nicety: the album's own page is still worth showing.
            }
        }

        var (rating, votes) = ReadRating(root);
        var links = ReadLinks(root, out _);
        return new AlbumInfo(
            Name,
            id,
            Str(root, "title") ?? query.Album,
            Str(root, "primary-type"),
            StrList(root, "secondary-types"),
            Str(root, "first-release-date") is { Length: > 0 } first ? first : date,
            ReadGenres(root),
            rating,
            votes,
            details.Label,
            details.Catalog,
            details.Country,
            details.Tracks,
            links,
            links.FirstOrDefault(l => l.Kind == "wikidata") is { } wikidata ? WikidataId(wikidata.Url) : null);
    }

    /// <summary>The id of the search hit whose <paramref name="field"/> equals <paramref name="wanted"/> (ignoring case and accents) with a good score.</summary>
    private async Task<string?> FindAsync(string resource, string listName, string lucene, string wanted, string field, CancellationToken ct)
    {
        using var doc = await GetAsync($"{resource}?query={Uri.EscapeDataString(lucene)}&limit=8", ct).ConfigureAwait(false);
        if (doc is null || !doc.RootElement.TryGetProperty(listName, out var hits) || hits.ValueKind != JsonValueKind.Array)
        {
            _log?.Invoke($"musicbrainz {resource} \"{wanted}\": search returned nothing");
            return null;
        }

        var target = InfoText.Normalize(wanted);
        _log?.Invoke($"musicbrainz {resource} \"{wanted}\": search returned {hits.GetArrayLength()} candidates");
        foreach (var hit in hits.EnumerateArray())
        {
            if (hit.TryGetProperty("score", out var score) && score.TryGetDouble(out var s) && s >= MinScore
                && InfoText.Normalize(Str(hit, field)) == target && Str(hit, "id") is { } id)
            {
                _log?.Invoke($"musicbrainz {resource} \"{wanted}\": taking {Str(hit, field)} ({id}, score {s:0})");
                return id;
            }
        }

        _log?.Invoke($"musicbrainz {resource} \"{wanted}\": no candidate has exactly that name with a score of {MinScore:0} or more");
        return null;
    }

    private static ArtistInfo ReadArtist(JsonElement root)
    {
        var members = new List<InfoMember>();
        var links = ReadLinks(root, out var images);
        if (root.TryGetProperty("relations", out var relations) && relations.ValueKind == JsonValueKind.Array)
        {
            foreach (var rel in relations.EnumerateArray())
            {
                if (Str(rel, "target-type") == "artist" && Str(rel, "type") == "member of band" && Str(rel, "direction") == "backward"
                    && rel.TryGetProperty("artist", out var person) && Str(person, "name") is { } name)
                {
                    var ended = rel.TryGetProperty("ended", out var e) && e.ValueKind == JsonValueKind.True;
                    var role = StrList(rel, "attributes");
                    members.Add(new InfoMember(name, role.Count > 0 ? string.Join(", ", role) : null, Str(rel, "begin"), Str(rel, "end"), !ended));
                }
            }
        }

        var life = root.TryGetProperty("life-span", out var span) ? span : default;
        var area = root.TryGetProperty("area", out var a) && a.ValueKind == JsonValueKind.Object ? Str(a, "name") : null;
        return new ArtistInfo(
            "musicbrainz",
            Str(root, "id") ?? "",
            Str(root, "name") ?? "",
            Str(root, "type"),
            Str(root, "country"),
            area,
            life.ValueKind == JsonValueKind.Object ? Str(life, "begin") : null,
            life.ValueKind == JsonValueKind.Object ? Str(life, "end") : null,
            life.ValueKind == JsonValueKind.Object && life.TryGetProperty("ended", out var ended2) && ended2.ValueKind == JsonValueKind.True,
            Str(root, "disambiguation"),
            Str(root, "annotation") is { } note ? PlainText(note) : null,
            ReadGenres(root),
            members.OrderByDescending(m => m.Current).ToList(),
            links,
            images,
            links.FirstOrDefault(l => l.Kind == "wikidata") is { } wikidata ? WikidataId(wikidata.Url) : null,
            Str(root, "annotation") is null ? null : "musicbrainz");
    }

    /// <summary>The web links of a page, and the addresses of its pictures (relations of type "image").</summary>
    private static List<InfoLink> ReadLinks(JsonElement root, out List<string> images)
    {
        var links = new List<InfoLink>();
        images = new List<string>();
        if (!root.TryGetProperty("relations", out var relations) || relations.ValueKind != JsonValueKind.Array)
        {
            return links;
        }

        foreach (var rel in relations.EnumerateArray())
        {
            if (Str(rel, "target-type") != "url" || !rel.TryGetProperty("url", out var url) || Str(url, "resource") is not { } resource)
            {
                continue;
            }

            if (Str(rel, "type") == "image")
            {
                if (ImageAddress(resource) is { } image)
                {
                    images.Add(image);
                }
            }
            else
            {
                links.Add(new InfoLink(Str(rel, "type") ?? "link", resource));
            }
        }

        return Rank(links);
    }

    private static readonly string[] LinkOrder = ["official homepage", "bandcamp", "wikipedia", "wikidata", "discogs", "allmusic", "social network", "youtube", "streaming", "free streaming", "lyrics"];

    /// <summary>The links with the useful kinds first and one link per kind (a page can carry thirteen "other databases").</summary>
    private static List<InfoLink> Rank(List<InfoLink> links) =>
        links.GroupBy(l => l.Kind)
            .Select(g => g.First())
            .OrderBy(l => Array.IndexOf(LinkOrder, l.Kind) is var i and >= 0 ? i : int.MaxValue)
            .ToList();

    /// <summary>
    /// The address that serves the picture a relation points to: a page of Wikimedia Commons ("…/wiki/File:Name.jpg") becomes its
    /// resizing redirect, a direct link to an image stays as it is, anything else is not a picture.
    /// </summary>
    internal static string? ImageAddress(string resource)
    {
        if (!Uri.TryCreate(resource, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        var at = path.IndexOf("/File:", StringComparison.Ordinal);
        if (uri.Host.EndsWith("wikimedia.org", StringComparison.OrdinalIgnoreCase) && at >= 0)
        {
            return "https://commons.wikimedia.org/wiki/Special:FilePath/" + Uri.EscapeDataString(path[(at + 6)..]) + "?width=900";
        }

        return path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
            ? resource
            : null;
    }

    private static List<string> ReadGenres(JsonElement root)
    {
        var names = Counted(root, "genres");
        if (names.Count == 0)
        {
            names = Counted(root, "tags");
        }

        return names.Take(MaxGenres).ToList();
    }

    /// <summary>The names of a list of {name, count} objects, the most voted first.</summary>
    private static List<string> Counted(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return list.EnumerateArray()
            .Select(g => (Name: Str(g, "name"), Count: g.TryGetProperty("count", out var c) && c.TryGetInt32(out var n) ? n : 0))
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .OrderByDescending(g => g.Count)
            .Select(g => g.Name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static (double? Rating, int Votes) ReadRating(JsonElement root)
    {
        if (root.TryGetProperty("rating", out var rating) && rating.ValueKind == JsonValueKind.Object
            && rating.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Number)
        {
            return (value.GetDouble(), rating.TryGetProperty("votes-count", out var votes) && votes.TryGetInt32(out var n) ? n : 0);
        }

        return (null, 0);
    }

    /// <summary>The entity id ("Q…") at the end of a Wikidata address, or null.</summary>
    internal static string? WikidataId(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Segments.Length > 0 && Regex.IsMatch(uri.Segments[^1], @"^Q\d+$") ? uri.Segments[^1] : null;

    /// <summary>The address of the entity's image (property P18), or null when it has none or Wikidata did not answer.</summary>
    private async Task<string?> WikidataImageAsync(string entity, CancellationToken ct)
    {
        try
        {
            var body = await InfoHttp.GetAsync(_http, $"https://www.wikidata.org/wiki/Special:EntityData/{entity}.json", "application/json", ct).ConfigureAwait(false);
            if (body is null)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var file = doc.RootElement.GetProperty("entities").GetProperty(entity).GetProperty("claims").GetProperty("P18")[0].GetProperty("mainsnak").GetProperty("datavalue").GetProperty("value").GetString();
            return string.IsNullOrWhiteSpace(file) ? null : "https://commons.wikimedia.org/wiki/Special:FilePath/" + Uri.EscapeDataString(file) + "?width=900";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return null; // a picture is a nicety
        }
    }

    /// <summary>The id and date of the earliest official release of a release group (else the earliest of any status).</summary>
    private static (string? Id, string? Date) FirstRelease(JsonElement root)
    {
        if (!root.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        var all = releases.EnumerateArray()
            .Select(r => (Id: Str(r, "id"), Date: Str(r, "date"), Official: Str(r, "status") == "Official"))
            .Where(r => r.Id is not null)
            .OrderByDescending(r => r.Official)
            .ThenBy(r => string.IsNullOrEmpty(r.Date) ? "9999" : r.Date, StringComparer.Ordinal)
            .FirstOrDefault();
        return (all.Id, all.Date);
    }

    private static (string? Label, string? Catalog, string? Country, int? Tracks) ReadRelease(JsonElement root)
    {
        string? label = null;
        string? catalog = null;
        if (root.TryGetProperty("label-info", out var infos) && infos.ValueKind == JsonValueKind.Array)
        {
            foreach (var info in infos.EnumerateArray())
            {
                label ??= info.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.Object ? Str(l, "name") : null;
                catalog ??= Str(info, "catalog-number");
            }
        }

        int? tracks = null;
        if (root.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Array)
        {
            var total = media.EnumerateArray().Sum(m => m.TryGetProperty("track-count", out var c) && c.TryGetInt32(out var n) ? n : 0);
            tracks = total > 0 ? total : null;
        }

        return (label, catalog, Str(root, "country"), tracks);
    }

    /// <summary>MusicBrainz annotations are wiki text: links and bold marks are reduced to their words.</summary>
    internal static string PlainText(string wiki) =>
        WikiMarkup().Replace(wiki, m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : "").Trim();

    private static string? Str(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static List<string> StrList(JsonElement element, string property) =>
        element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];

    /// <summary>One request, after waiting for this client's turn. Null for a 404.</summary>
    private async Task<JsonDocument?> GetAsync(string path, CancellationToken ct)
    {
        await _turn.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var wait = _lastRequest + _gap - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct).ConfigureAwait(false);
            }

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    var body = await InfoHttp.GetAsync(_http, Base + path + (path.Contains('?') ? "&" : "?") + "fmt=json", "application/json", ct).ConfigureAwait(false);
                    return body is null ? null : JsonDocument.Parse(body);
                }
                catch (HttpRequestException ex) when (attempt == 1 && ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    // MusicBrainz answers 503 when it is busy or the limit was passed: once more after a pause.
                    await Task.Delay(_gap + _gap, ct).ConfigureAwait(false);
                }
                finally
                {
                    _lastRequest = DateTime.UtcNow;
                }
            }
        }
        finally
        {
            _turn.Release();
        }
    }
}
