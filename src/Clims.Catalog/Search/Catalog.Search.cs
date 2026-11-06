namespace Clims.Catalog;

public sealed partial class Catalog
{
    /// <summary>Default number of hits kept per group (the UI shows the top few and offers "More").</summary>
    public const int DefaultMaxArtists = 50;

    /// <inheritdoc cref="DefaultMaxArtists"/>
    public const int DefaultMaxAlbums = 100;

    /// <inheritdoc cref="DefaultMaxArtists"/>
    public const int DefaultMaxTracks = 200;

    /// <inheritdoc cref="DefaultMaxArtists"/>
    public const int DefaultMaxGenres = 30;

    /// <summary>
    /// Ranked search over artists, albums, tracks and genres (see <see cref="SearchRanking"/> for the rules): every query
    /// word must occur in the name, in any order, anywhere in a word, ignoring case and accents. About 20 000 tracks take
    /// a few milliseconds. Tracks are only searched as far as they are loaded (<see cref="TracksLoaded"/>).
    /// </summary>
    public RankedSearchResult Search(
        string text,
        int maxArtists = DefaultMaxArtists,
        int maxAlbums = DefaultMaxAlbums,
        int maxTracks = DefaultMaxTracks,
        int maxGenres = DefaultMaxGenres) =>
        Search(SearchQuery.Parse(text), maxArtists, maxAlbums, maxTracks, maxGenres);

    /// <inheritdoc cref="Search(string,int,int,int,int)"/>
    public RankedSearchResult Search(SearchQuery query, int maxArtists, int maxAlbums, int maxTracks, int maxGenres)
    {
        if (query.IsEmpty)
        {
            return RankedSearchResult.Empty;
        }

        var tracks = TrackIdx();
        return new RankedSearchResult(
            SearchRanking.Rank(_artists, _artistKeys, query, maxArtists),
            SearchRanking.Rank(_albums, _albumKeys, query, maxAlbums),
            SearchRanking.Rank(tracks.Items, tracks.Keys, query, maxTracks),
            SearchRanking.Rank(_genres, _genreKeys, query, maxGenres),
            query.Words);
    }

    private TrackIndex TrackIdx()
    {
        lock (_gate)
        {
            if (_trackIndex is { } cached)
            {
                return cached;
            }

            var items = new List<CatalogTrack>(_tracks.Values.Sum(t => t.Length));
            var keys = new List<SearchKeys>(items.Capacity);
            foreach (var (albumId, albumTracks) in _tracks)
            {
                items.AddRange(albumTracks);
                keys.AddRange(_trackKeys[albumId]);
            }

            return _trackIndex = new TrackIndex(items, keys);
        }
    }

    private sealed record TrackIndex(IReadOnlyList<CatalogTrack> Items, IReadOnlyList<SearchKeys> Keys);
}
