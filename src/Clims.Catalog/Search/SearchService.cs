using Clims.Lms;

namespace Clims.Catalog;

/// <summary>Search over the local index, with the server filling in the groups the index does not have yet.</summary>
public interface ISearchService
{
    /// <summary>Raised with the local result at once, and again when the server's part has arrived (any thread).</summary>
    event Action<SearchOutcome>? Changed;

    /// <summary>Starts a search. Supersedes the previous one: an answer for older text is never reported.</summary>
    void Query(string text);
}

/// <summary>Settings for <see cref="SearchService"/>.</summary>
public sealed class SearchServiceOptions
{
    /// <summary>Wait after the last keystroke before the server is asked (the local result needs no wait).</summary>
    public TimeSpan ServerDebounce { get; init; } = TimeSpan.FromMilliseconds(120);

    /// <summary>Hits requested per group from the server.</summary>
    public int ServerCount { get; init; } = 50;
}

/// <summary>
/// Local first, server for what is missing. The local index answers every keystroke synchronously (a few milliseconds).
/// While the index is incomplete the server's <c>search</c> fills the gaps, after a short debounce and with the previous
/// request cancelled: before the first sync everything comes from the server; while a first sync is still loading tracks,
/// only tracks do. A server that cannot be reached leaves the local result and adds a note.
/// </summary>
public sealed class SearchService : ISearchService, IDisposable
{
    private static readonly TrackTags ServerTrackTags =
        TrackTags.Artist | TrackTags.ArtistId | TrackTags.Album | TrackTags.AlbumId | TrackTags.TrackNumber | TrackTags.Disc | TrackTags.Duration | TrackTags.ArtworkTrackId;

    private static readonly AlbumTags ServerAlbumTags =
        AlbumTags.Album | AlbumTags.Artist | AlbumTags.ArtistId | AlbumTags.Year | AlbumTags.ArtworkTrackId | AlbumTags.Compilation | AlbumTags.DiscCount;

    private readonly Func<Catalog?> _catalog;
    private readonly ILibraryApi? _server;
    private readonly SearchServiceOptions _options;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private long _sequence;

    /// <summary>Creates the service. <paramref name="server"/> is null when there is no server (demo): then only the index answers.</summary>
    public SearchService(Func<Catalog?> catalog, ILibraryApi? server, SearchServiceOptions? options = null)
    {
        _catalog = catalog;
        _server = server;
        _options = options ?? new SearchServiceOptions();
    }

    /// <inheritdoc />
    public event Action<SearchOutcome>? Changed;

    /// <inheritdoc />
    public void Query(string text)
    {
        text = (text ?? string.Empty).Trim();
        CancellationToken token;
        long sequence;
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            token = _cts.Token;
            sequence = ++_sequence;
        }

        var query = SearchQuery.Parse(text);
        var catalog = _catalog();
        var local = catalog is null || query.IsEmpty ? RankedSearchResult.Empty with { Words = query.Words } : catalog.Search(query, Catalog.DefaultMaxArtists, Catalog.DefaultMaxAlbums, Catalog.DefaultMaxTracks, Catalog.DefaultMaxGenres);
        var missing = query.IsEmpty ? SearchGroups.None : Missing(catalog);
        var willAsk = missing != SearchGroups.None && _server is not null;

        Changed?.Invoke(new SearchOutcome(text, local, missing, willAsk, null));
        if (!willAsk)
        {
            return;
        }

        _ = AskServerAsync(text, query, local, missing, sequence, token);
    }

    /// <summary>Stops any search that is still waiting for the server.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>The groups the local index cannot answer yet.</summary>
    public static SearchGroups Missing(Catalog? catalog)
    {
        if (catalog is null || catalog.Albums.Count == 0)
        {
            return SearchGroups.All;
        }

        return catalog.TracksLoaded ? SearchGroups.None : SearchGroups.Tracks;
    }

    private async Task AskServerAsync(string text, SearchQuery query, RankedSearchResult local, SearchGroups missing, long sequence, CancellationToken token)
    {
        SearchOutcome outcome;
        try
        {
            await Task.Delay(_options.ServerDebounce, token).ConfigureAwait(false);
            var server = await FetchAsync(text, query, missing, token).ConfigureAwait(false);
            outcome = new SearchOutcome(text, Merge(local, server, missing), missing, false, null);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (LmsException ex)
        {
            outcome = new SearchOutcome(text, local, missing, false, "The server could not be reached, showing what the local library has. " + ex.Message);
        }

        // A newer keystroke has superseded this answer: never show it.
        lock (_gate)
        {
            if (sequence != _sequence || token.IsCancellationRequested)
            {
                return;
            }
        }

        Changed?.Invoke(outcome);
    }

    private async Task<RankedSearchResult> FetchAsync(string text, SearchQuery query, SearchGroups missing, CancellationToken token)
    {
        var count = _options.ServerCount;
        var server = _server!;

        var artists = missing.HasFlag(SearchGroups.Artists)
            ? server.GetArtistsAsync(new ArtistQuery { Search = text }, 0, count, token)
            : null;
        var albums = missing.HasFlag(SearchGroups.Albums)
            ? server.GetAlbumsAsync(new AlbumQuery { Search = text, Tags = ServerAlbumTags }, 0, count, token)
            : null;
        var tracks = missing.HasFlag(SearchGroups.Tracks)
            ? server.GetTracksAsync(new TrackQuery { Search = text, Tags = ServerTrackTags, Sort = TrackSort.AlbumTrack }, 0, count, token)
            : null;
        var genres = missing.HasFlag(SearchGroups.Genres)
            ? server.SearchAsync(text, 10, token)
            : null;

        var artistPage = artists is null ? null : await artists.ConfigureAwait(false);
        var albumPage = albums is null ? null : await albums.ConfigureAwait(false);
        var trackPage = tracks is null ? null : await tracks.ConfigureAwait(false);
        var genreHits = genres is null ? null : await genres.ConfigureAwait(false);

        // Rank what the server sent with the same rules as the local index, so the order does not depend on the source.
        var artistItems = artistPage?.Items.Select(a => new CatalogArtist(a.Id, a.Name)).ToList() ?? new List<CatalogArtist>();
        var albumItems = albumPage?.Items
            .Select(a => new CatalogAlbum(a.Id, a.Title, a.ArtistId, a.ArtistName ?? string.Empty, a.Year, a.ArtworkTrackId, a.IsCompilation, a.DiscCount))
            .ToList() ?? new List<CatalogAlbum>();
        var trackSource = trackPage?.Items.Where(t => t.AlbumId is not null).ToList() ?? new List<Track>();
        var trackItems = trackSource
            .Select(t => new CatalogTrack(t.Id, t.AlbumId!.Value, t.Title, t.Artist, t.ArtistId, t.DiscNumber ?? 1, t.TrackNumber ?? 0, t.Duration, t.Genre))
            .ToList();
        var genreItems = genreHits?.Genres.Select(g => new CatalogGenre(g.Id, g.Name)).ToList() ?? new List<CatalogGenre>();

        return new RankedSearchResult(
            artistPage is null ? SearchGroup<CatalogArtist>.None : SearchRanking.Rank(artistItems, artistItems.Select(a => SearchKeys.ForName(a.Name)).ToList(), query, count) with { Total = Math.Max(artistPage.Total, artistItems.Count) },
            albumPage is null ? SearchGroup<CatalogAlbum>.None : SearchRanking.Rank(albumItems, albumItems.Select(a => SearchKeys.ForAlbum(a.Title, a.ArtistName)).ToList(), query, count) with { Total = Math.Max(albumPage.Total, albumItems.Count) },
            trackPage is null ? SearchGroup<CatalogTrack>.None : SearchRanking.Rank(trackItems, trackSource.Select(t => SearchKeys.ForTrack(t.Title, t.Artist, t.Album)).ToList(), query, count) with { Total = Math.Max(trackPage.Total, trackItems.Count) },
            genreHits is null ? SearchGroup<CatalogGenre>.None : SearchRanking.Rank(genreItems, genreItems.Select(g => SearchKeys.ForName(g.Name)).ToList(), query, count) with { Total = Math.Max(genreHits.GenreCount, genreItems.Count) },
            query.Words,
            trackSource.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => new TrackExtra(g.First().Album, g.First().ArtworkTrackId)));
    }

    private static RankedSearchResult Merge(RankedSearchResult local, RankedSearchResult server, SearchGroups missing) => new(
        missing.HasFlag(SearchGroups.Artists) ? server.Artists : local.Artists,
        missing.HasFlag(SearchGroups.Albums) ? server.Albums : local.Albums,
        missing.HasFlag(SearchGroups.Tracks) ? server.Tracks : local.Tracks,
        missing.HasFlag(SearchGroups.Genres) ? server.Genres : local.Genres,
        local.Words,
        missing.HasFlag(SearchGroups.Tracks) ? server.TrackExtras : local.TrackExtras);
}
