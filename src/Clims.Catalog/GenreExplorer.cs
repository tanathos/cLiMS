namespace Clims.Catalog;

/// <summary>
/// The questions behind the Genres explorer: which genres go together, which tracks and artists match a chain of genres,
/// and a random pick of those tracks. A track matches a chain when it is tagged with every genre in it. Built once per
/// catalogue; every query is a set intersection over a few tens of thousands of tracks.
/// </summary>
public sealed class GenreExplorer
{
    private readonly Catalog _catalog;
    private readonly Dictionary<int, string> _names;
    private readonly Dictionary<int, int[]> _genresOfTrack = new();
    private readonly Dictionary<int, List<CatalogTrack>> _tracksOfGenre = new();
    private readonly List<CatalogTrack> _all;

    private GenreExplorer(Catalog catalog)
    {
        _catalog = catalog;
        _names = catalog.Genres.ToDictionary(g => g.Id, g => g.Name);
        _all = catalog.SnapshotTracks().ToList();

        if (catalog.GenreTracks is { } links)
        {
            var known = new Dictionary<int, CatalogTrack>(_all.Count);
            foreach (var t in _all)
            {
                known[t.Id] = t;
            }

            var lists = new Dictionary<int, List<int>>();
            foreach (var (genreId, trackIds) in links)
            {
                foreach (var trackId in trackIds)
                {
                    if (!known.TryGetValue(trackId, out var track))
                    {
                        continue;
                    }

                    (_tracksOfGenre.TryGetValue(genreId, out var l) ? l : _tracksOfGenre[genreId] = new List<CatalogTrack>()).Add(track);
                    (lists.TryGetValue(trackId, out var g) ? g : lists[trackId] = new List<int>()).Add(genreId);
                }
            }

            foreach (var (trackId, genres) in lists)
            {
                _genresOfTrack[trackId] = genres.ToArray();
            }
        }
        else
        {
            // An index from before every genre was stored: the one genre name each track has.
            var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in catalog.Genres)
            {
                byName.TryAdd(g.Name, g.Id);
            }

            foreach (var t in _all)
            {
                if (t.Genre is { } name && byName.TryGetValue(name, out var id))
                {
                    _genresOfTrack[t.Id] = new[] { id };
                    (_tracksOfGenre.TryGetValue(id, out var l) ? l : _tracksOfGenre[id] = new List<CatalogTrack>()).Add(t);
                }
            }
        }
    }

    /// <summary>True when each track's full genre list is known; false when only its one genre name is (resync to fix).</summary>
    public bool HasAllGenres => _catalog.GenreTracks is not null;

    /// <summary>Builds the explorer for a catalogue.</summary>
    public static GenreExplorer Build(Catalog catalog) => new(catalog);

    /// <summary>How many genres a track is tagged with.</summary>
    public int GenreCount(int trackId) => _genresOfTrack.TryGetValue(trackId, out var g) ? g.Length : 0;

    /// <summary>Name of a genre ("" when unknown).</summary>
    public string NameOf(int genreId) => _names.GetValueOrDefault(genreId, string.Empty);

    /// <summary>The tracks tagged with every genre of <paramref name="chain"/> (every track for an empty chain).</summary>
    public IReadOnlyList<CatalogTrack> Match(IReadOnlyList<int> chain)
    {
        if (chain.Count == 0)
        {
            return _all;
        }

        List<CatalogTrack>? smallest = null;
        foreach (var id in chain)
        {
            if (!_tracksOfGenre.TryGetValue(id, out var list))
            {
                return Array.Empty<CatalogTrack>();
            }

            if (smallest is null || list.Count < smallest.Count)
            {
                smallest = list;
            }
        }

        if (chain.Count == 1)
        {
            return smallest!;
        }

        return smallest!.Where(t => _genresOfTrack.TryGetValue(t.Id, out var genres) && chain.All(c => Array.IndexOf(genres, c) >= 0)).ToList();
    }

    /// <summary>
    /// The genres that go with <paramref name="chain"/>: those carried by at least one matching track, with their track
    /// counts. The chain's own genres are left out. The first level (empty chain) is alphabetical, the others most
    /// tracks first.
    /// </summary>
    public IReadOnlyList<GenreOption> Options(IReadOnlyList<int> chain)
    {
        var counts = new Dictionary<int, int>();
        foreach (var t in Match(chain))
        {
            if (!_genresOfTrack.TryGetValue(t.Id, out var genres))
            {
                continue;
            }

            foreach (var g in genres)
            {
                if (!chain.Contains(g))
                {
                    counts[g] = counts.GetValueOrDefault(g) + 1;
                }
            }
        }

        var options = counts.Select(c => new GenreOption(c.Key, NameOf(c.Key), c.Value));
        return (chain.Count == 0
                ? options.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                : options.OrderByDescending(o => o.Tracks).ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Artists of the matching tracks (the track's artist, else its album's), most tracks first.</summary>
    public IReadOnlyList<ArtistOption> Artists(IReadOnlyList<int> chain)
    {
        var map = new Dictionary<string, (string Name, int Tracks, int AlbumId)>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in Match(chain))
        {
            var name = !string.IsNullOrWhiteSpace(t.Artist) ? t.Artist! : _catalog.Album(t.AlbumId)?.ArtistName ?? string.Empty;
            if (name.Length == 0)
            {
                continue;
            }

            map[name] = map.TryGetValue(name, out var e) ? (e.Name, e.Tracks + 1, e.AlbumId) : (name, 1, t.AlbumId);
        }

        return map.Values
            .OrderByDescending(e => e.Tracks)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e => new ArtistOption(e.Name, e.Tracks, e.AlbumId))
            .ToList();
    }

    /// <summary>Up to <paramref name="count"/> different matching tracks, picked at random (all of them when fewer match).</summary>
    public IReadOnlyList<CatalogTrack> Random(IReadOnlyList<int> chain, int count, Random random)
    {
        var pool = Match(chain).ToArray();
        var take = Math.Min(Math.Max(0, count), pool.Length);
        for (var i = 0; i < take; i++)
        {
            var j = random.Next(i, pool.Length);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        return pool.Take(take).ToList();
    }
}
