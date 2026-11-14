using System.Text.Json;

namespace Clims.Info;

/// <summary>
/// Asks the providers in order (each is told what the earlier ones found, and their answers are merged, the earlier one first),
/// remembers what they said in the <see cref="InfoStore"/>, and fetches
/// pictures. Looking things up is always something the user asked for: nothing here runs in the background.
/// </summary>
public sealed class InfoService
{
    /// <summary>How long a stored answer is used before the providers are asked again.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private const int MaxImageBytes = 8 * 1024 * 1024;

    private readonly IReadOnlyList<IInfoProvider> _providers;
    private readonly InfoStore? _store;
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string>? _log;

    /// <summary>Creates the service.</summary>
    /// <param name="providers">The providers, the one to ask first at the front.</param>
    /// <param name="store">Where answers are kept, or null for no cache.</param>
    /// <param name="http">Client for the pictures (it must not carry credentials meant for another server).</param>
    /// <param name="clock">The present moment (tests pass their own).</param>
    /// <param name="log">Receives a line for each lookup: what was asked, what every provider answered (null: no log).</param>
    public InfoService(IReadOnlyList<IInfoProvider> providers, InfoStore? store, HttpClient http, Func<DateTimeOffset>? clock = null, Action<string>? log = null)
    {
        _providers = providers;
        _store = store;
        _http = http;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _log = log;
    }

    /// <summary>What is known about the artist, or null when no provider knows it. Every provider is asked, and what they know is merged.</summary>
    /// <param name="query">What to look up.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <param name="refresh">Ask the providers even when a stored answer exists, and store the new one.</param>
    /// <exception cref="HttpRequestException">No provider knew the artist and one could not be reached.</exception>
    public Task<ArtistInfo?> ArtistAsync(InfoQuery query, CancellationToken ct, bool refresh = false) =>
        AskAsync<ArtistInfo>($"Info artist \"{query.Artist}\"", "artist2|" + InfoText.Normalize(query.Artist), refresh, (p, so_far) => p.GetArtistAsync(so_far is null ? query : query with { WikidataId = so_far.WikidataId }, ct), MergeArtists, ct);

    /// <summary>What is known about the album, or null when no provider knows it. Every provider is asked, and what they know is merged.</summary>
    /// <param name="query">What to look up; give it the artist's id when the artist was found.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <param name="refresh">Ask the providers even when a stored answer exists, and store the new one.</param>
    /// <exception cref="HttpRequestException">No provider knew the album and one could not be reached.</exception>
    public Task<AlbumInfo?> AlbumAsync(InfoQuery query, CancellationToken ct, bool refresh = false) =>
        AskAsync<AlbumInfo>($"Info album \"{query.Artist} – {query.Album}\"", "album2|" + InfoText.Normalize(query.Artist) + "|" + InfoText.Normalize(query.Album), refresh, (p, so_far) => p.GetAlbumAsync(so_far is null ? query : query with { AlbumWikidataId = so_far.WikidataId }, ct), MergeAlbums, ct);

    /// <summary>The bytes of a picture, from the store or the web; null when the address does not serve one.</summary>
    /// <param name="url">Address of the picture.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <param name="refresh">Download it again even when it is stored.</param>
    public async Task<byte[]?> ImageAsync(string url, CancellationToken ct, bool refresh = false)
    {
        var key = "image|" + url;
        if (!refresh && _store?.GetBytes(key, Lifetime, _clock()) is { } stored)
        {
            return stored;
        }

        var bytes = await InfoHttp.GetAsync(_http, url, "image/*", ct).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0 || bytes.Length > MaxImageBytes)
        {
            return null;
        }

        _store?.PutBytes(key, bytes, _clock());
        return bytes;
    }

    /// <summary>
    /// Asks every provider, handing each what the earlier ones found. A result built while a provider could not be reached is not
    /// stored: it would keep the missing part away for the whole lifetime.
    /// </summary>
    private async Task<T?> AskAsync<T>(string what, string key, bool refresh, Func<IInfoProvider, T?, Task<T?>> ask, Func<T, T, T> merge, CancellationToken ct)
        where T : class
    {
        if (!refresh && _store?.GetText(key, Lifetime, _clock()) is { } json)
        {
            try
            {
                if (JsonSerializer.Deserialize<T>(json) is { } cached)
                {
                    _log?.Invoke($"{what}: stored answer used (not older than {Lifetime.TotalDays:0} days)");
                    return cached;
                }
            }
            catch (JsonException)
            {
                // An entry from an older layout: ask again.
            }
        }

        _log?.Invoke($"{what}: asking {string.Join(", ", _providers.Select(p => p.Name))}{(refresh ? " (refresh)" : "")}");
        T? found = null;
        HttpRequestException? failure = null;
        foreach (var provider in _providers)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await ask(provider, found).ConfigureAwait(false) is { } answer)
                {
                    _log?.Invoke($"{what}: {provider.Name} matched{(found is null ? "" : ", merged with the earlier answers")}");
                    found = found is null ? answer : merge(found, answer);
                }
                else
                {
                    _log?.Invoke($"{what}: {provider.Name} has no match");
                }
            }
            catch (HttpRequestException ex)
            {
                _log?.Invoke($"{what}: {provider.Name} could not be reached ({ex.Message})");
                failure = ex;
            }
        }

        if (found is null)
        {
            _log?.Invoke($"{what}: nothing found{(failure is null ? "" : " (a provider could not be reached)")}");
            return failure is null ? null : throw failure;
        }

        if (failure is null)
        {
            _store?.PutText(key, JsonSerializer.Serialize(found), _clock());
        }

        return found;
    }

    /// <summary>The first answer, with what it lacks filled in from the second; the longer description wins.</summary>
    private static AlbumInfo MergeAlbums(AlbumInfo first, AlbumInfo second)
    {
        var secondText = !string.IsNullOrEmpty(second.Description) && (string.IsNullOrEmpty(first.Description) || second.Description.Length > first.Description.Length);
        return first with
        {
            Provider = first.Provider + ", " + second.Provider,
            Kind = first.Kind ?? second.Kind,
            Secondary = first.Secondary.Count > 0 ? first.Secondary : second.Secondary,
            FirstReleased = first.FirstReleased ?? second.FirstReleased,
            Genres = first.Genres.Count > 0 ? first.Genres : second.Genres,
            Rating = first.Rating ?? second.Rating,
            Votes = first.Rating is null ? second.Votes : first.Votes,
            Label = first.Label ?? second.Label,
            CatalogNumber = first.CatalogNumber ?? second.CatalogNumber,
            Country = first.Country ?? second.Country,
            TrackCount = first.TrackCount ?? second.TrackCount,
            Links = first.Links.Concat(second.Links).GroupBy(l => l.Kind).Select(g => g.First()).ToList(),
            WikidataId = first.WikidataId ?? second.WikidataId,
            Description = secondText ? second.Description : first.Description,
            DescriptionSource = secondText ? second.DescriptionSource : first.DescriptionSource,
        };
    }

    /// <summary>The first answer, with what it lacks filled in from the second; the longer biography wins.</summary>
    private static ArtistInfo MergeArtists(ArtistInfo first, ArtistInfo second)
    {
        var secondBio = !string.IsNullOrEmpty(second.Biography) && (string.IsNullOrEmpty(first.Biography) || second.Biography.Length > first.Biography.Length);
        return first with
        {
            Provider = first.Provider + ", " + second.Provider,
            Kind = first.Kind ?? second.Kind,
            Country = first.Country ?? second.Country,
            Area = first.Area ?? second.Area,
            Begin = first.Begin ?? second.Begin,
            End = first.End ?? second.End,
            Disambiguation = first.Disambiguation ?? second.Disambiguation,
            Biography = secondBio ? second.Biography : first.Biography,
            BiographySource = secondBio ? second.BiographySource : first.BiographySource,
            Genres = first.Genres.Count > 0 ? first.Genres : second.Genres,
            Members = first.Members.Count > 0 ? first.Members : second.Members,
            Links = first.Links.Concat(second.Links).GroupBy(l => l.Kind).Select(g => g.First()).ToList(),
            ImageUrls = first.ImageUrls.Concat(second.ImageUrls).Distinct().ToList(),
            WikidataId = first.WikidataId ?? second.WikidataId,
        };
    }
}
