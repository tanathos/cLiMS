namespace Clims.Catalog;

/// <summary>
/// The in-memory read model of the library: what the Library tab navigates. Built from the SQLite index or from a
/// sync. Artists, genres and albums are fixed; the track list of one album can be replaced (on-view refresh), which
/// is the only mutation and is thread-safe.
/// </summary>
public sealed partial class Catalog
{
    private readonly object _gate = new();
    private readonly List<CatalogGenre> _genres;
    private readonly List<CatalogArtist> _artists;
    private readonly List<CatalogAlbum> _albums;
    private readonly Dictionary<int, CatalogAlbum> _albumById;
    private readonly Dictionary<int, List<CatalogAlbum>> _albumsByArtist = new();
    private readonly List<CatalogAlbum> _albumsSorted;
    private readonly Dictionary<int, CatalogTrack[]> _tracks = new();
    private readonly Dictionary<int, SearchKeys[]> _trackKeys = new();
    private readonly Dictionary<int, AlbumSummary> _summaries = new();
    private readonly SearchKeys[] _artistKeys;
    private readonly SearchKeys[] _albumKeys;
    private readonly SearchKeys[] _genreKeys;
    private TrackIndex? _trackIndex;
    private GenreIndex? _genreIndex;
    private int _version;

    /// <summary>Builds the model. Lists keep the order given; tracks are grouped by album and sorted by disc and number.</summary>
    public Catalog(
        IEnumerable<CatalogGenre> genres,
        IEnumerable<CatalogArtist> artists,
        IEnumerable<CatalogAlbum> albums,
        IEnumerable<CatalogTrack> tracks,
        CatalogMeta? meta,
        bool tracksLoaded = true,
        IReadOnlyDictionary<int, int[]>? genreTracks = null)
    {
        _genres = genres.ToList();
        _artists = artists.ToList();
        _albums = albums.ToList();
        Meta = meta;
        TracksLoaded = tracksLoaded;
        GenreTracks = genreTracks;

        _albumById = new Dictionary<int, CatalogAlbum>(_albums.Count);
        foreach (var album in _albums)
        {
            _albumById[album.Id] = album;
            if (album.ArtistId is { } artistId)
            {
                if (!_albumsByArtist.TryGetValue(artistId, out var list))
                {
                    _albumsByArtist[artistId] = list = new List<CatalogAlbum>();
                }

                list.Add(album);
            }
        }

        foreach (var list in _albumsByArtist.Values)
        {
            list.Sort(CompareByYearThenTitle);
        }

        var artistOrder = new Dictionary<int, int>();
        for (var i = 0; i < _artists.Count; i++)
        {
            artistOrder[_artists[i].Id] = i;
        }

        _albumsSorted = _albums
            .OrderBy(a => a.ArtistId is { } id && artistOrder.TryGetValue(id, out var o) ? o : int.MaxValue)
            .ThenBy(a => a.Year == 0 ? int.MaxValue : a.Year)
            .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in tracks.GroupBy(t => t.AlbumId))
        {
            SetTracks(group.Key, group.ToArray());
        }

        _artistKeys = _artists.Select(a => SearchKeys.ForName(a.Name)).ToArray();
        _albumKeys = _albums.Select(a => SearchKeys.ForAlbum(a.Title, a.ArtistName)).ToArray();
        _genreKeys = _genres.Select(g => SearchKeys.ForName(g.Name)).ToArray();
    }

    /// <summary>An empty catalogue (nothing synced yet).</summary>
    public static Catalog Empty { get; } = new(
        Array.Empty<CatalogGenre>(),
        Array.Empty<CatalogArtist>(),
        Array.Empty<CatalogAlbum>(),
        Array.Empty<CatalogTrack>(),
        null,
        false);

    /// <summary>What the server looked like when this index was built; null for a partial in-memory catalogue.</summary>
    public CatalogMeta? Meta { get; }

    /// <summary>False while a first sync has loaded artists and albums but not yet the tracks.</summary>
    public bool TracksLoaded { get; }

    /// <summary>
    /// Every track of each genre, by genre id: a track can carry several genres, but the sync only reads one per track, so
    /// this comes from asking the server track by genre. Null in an index made before this was stored (a resync fills it).
    /// </summary>
    public IReadOnlyDictionary<int, int[]>? GenreTracks { get; }

    /// <summary>Increases whenever an album's tracks are replaced, so views can tell something changed.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>Album artists in server order.</summary>
    public IReadOnlyList<CatalogArtist> Artists => _artists;

    /// <summary>Genres in server order.</summary>
    public IReadOnlyList<CatalogGenre> Genres => _genres;

    /// <summary>Every album, ordered by artist (server order), year, then title.</summary>
    public IReadOnlyList<CatalogAlbum> Albums => _albumsSorted;

    /// <summary>Total number of tracks known right now.</summary>
    public int TrackCount
    {
        get
        {
            lock (_gate)
            {
                return _tracks.Values.Sum(t => t.Length);
            }
        }
    }

    /// <summary>A copy of every track currently known, for persisting the catalogue.</summary>
    public IReadOnlyList<CatalogTrack> SnapshotTracks()
    {
        lock (_gate)
        {
            return _tracks.Values.SelectMany(t => t).ToList();
        }
    }

    /// <summary>Finds an album by id.</summary>
    public CatalogAlbum? Album(int albumId) => _albumById.GetValueOrDefault(albumId);

    /// <summary>Albums of an artist, oldest first (unknown year last), then by title.</summary>
    public IReadOnlyList<CatalogAlbum> AlbumsOfArtist(int artistId) =>
        _albumsByArtist.TryGetValue(artistId, out var list) ? list : Array.Empty<CatalogAlbum>();

    /// <summary>Number of albums of an artist.</summary>
    public int AlbumCountOf(int artistId) => AlbumsOfArtist(artistId).Count;

    /// <summary>Albums that have at least one track of the genre. Needs tracks; empty before they are loaded.</summary>
    public IReadOnlyList<CatalogAlbum> AlbumsOfGenre(string genre) =>
        Genre().AlbumsByGenre.TryGetValue(genre, out var list) ? list : Array.Empty<CatalogAlbum>();

    /// <summary>Release years that occur, newest first; 0 stands for "unknown".</summary>
    public IReadOnlyList<int> Years => _albums.Select(a => a.Year).Distinct().OrderByDescending(y => y == 0 ? -1 : y).ToList();

    /// <summary>Albums from a year (0 = unknown year), by artist then title.</summary>
    public IReadOnlyList<CatalogAlbum> AlbumsOfYear(int year) => _albumsSorted.Where(a => a.Year == year).ToList();

    /// <summary>Tracks of an album in disc and track order. Empty until loaded by a sync or an on-view refresh.</summary>
    public IReadOnlyList<CatalogTrack> TracksOf(int albumId)
    {
        lock (_gate)
        {
            return _tracks.TryGetValue(albumId, out var t) ? t : Array.Empty<CatalogTrack>();
        }
    }

    /// <summary>Track count, total length and genres of an album, from the tracks known so far.</summary>
    public AlbumSummary Summary(int albumId)
    {
        lock (_gate)
        {
            if (_summaries.TryGetValue(albumId, out var cached))
            {
                return cached;
            }

            var tracks = _tracks.TryGetValue(albumId, out var t) ? t : Array.Empty<CatalogTrack>();
            var genres = tracks.Where(x => !string.IsNullOrWhiteSpace(x.Genre))
                .GroupBy(x => x.Genre!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToList();
            var summary = new AlbumSummary(tracks.Length, TimeSpan.FromTicks(tracks.Sum(x => x.Duration.Ticks)), genres);
            _summaries[albumId] = summary;
            return summary;
        }
    }

    /// <summary>Number of albums per genre name (from known tracks).</summary>
    public int AlbumCountOfGenre(string genre) => AlbumsOfGenre(genre).Count;

    /// <summary>
    /// Replaces the tracks of one album (on-view refresh). Returns true when they actually differ, so callers can skip
    /// a redraw. An empty list is accepted: it means the album lost its tracks.
    /// </summary>
    public bool ReplaceAlbumTracks(int albumId, IReadOnlyList<CatalogTrack> tracks)
    {
        if (!_albumById.ContainsKey(albumId))
        {
            return false;
        }

        var next = tracks.OrderBy(t => t.Disc).ThenBy(t => t.Number).ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase).ToArray();
        lock (_gate)
        {
            if (_tracks.TryGetValue(albumId, out var old) && old.AsSpan().SequenceEqual(next))
            {
                return false;
            }

            SetTracks(albumId, next);
            _summaries.Remove(albumId);
            _genreIndex = null;
            Interlocked.Increment(ref _version);
            return true;
        }
    }

    private static int CompareByYearThenTitle(CatalogAlbum a, CatalogAlbum b)
    {
        var ya = a.Year == 0 ? int.MaxValue : a.Year;
        var yb = b.Year == 0 ? int.MaxValue : b.Year;
        var c = ya.CompareTo(yb);
        return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(a.Title, b.Title);
    }

    private void SetTracks(int albumId, CatalogTrack[] tracks)
    {
        Array.Sort(tracks, (a, b) =>
        {
            var c = a.Disc.CompareTo(b.Disc);
            if (c == 0)
            {
                c = a.Number.CompareTo(b.Number);
            }

            return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(a.Title, b.Title);
        });
        _tracks[albumId] = tracks;
        var albumTitle = _albumById.GetValueOrDefault(albumId)?.Title;
        _trackKeys[albumId] = tracks.Select(t => SearchKeys.ForTrack(t.Title, t.Artist, albumTitle)).ToArray();
        _trackIndex = null;
    }

    private GenreIndex Genre()
    {
        lock (_gate)
        {
            return _genreIndex ??= GenreIndex.Build(_albumById, _tracks);
        }
    }

    private sealed class GenreIndex
    {
        public required Dictionary<string, List<CatalogAlbum>> AlbumsByGenre { get; init; }

        public static GenreIndex Build(Dictionary<int, CatalogAlbum> albums, Dictionary<int, CatalogTrack[]> tracks)
        {
            var map = new Dictionary<string, List<CatalogAlbum>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (albumId, albumTracks) in tracks)
            {
                if (!albums.TryGetValue(albumId, out var album))
                {
                    continue;
                }

                foreach (var genre in albumTracks.Select(t => t.Genre).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!map.TryGetValue(genre!, out var list))
                    {
                        map[genre!] = list = new List<CatalogAlbum>();
                    }

                    list.Add(album);
                }
            }

            foreach (var list in map.Values)
            {
                list.Sort((a, b) => string.Compare(a.ArtistName, b.ArtistName, StringComparison.OrdinalIgnoreCase) is var c and not 0
                    ? c
                    : CompareByYearThenTitle(a, b));
            }

            return new GenreIndex { AlbumsByGenre = map };
        }
    }
}
