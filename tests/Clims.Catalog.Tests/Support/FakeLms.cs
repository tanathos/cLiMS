using System.Threading.Channels;
using Clims.Lms;

namespace Clims.Catalog.Tests;

/// <summary>An in-memory LMS: library data, server status, and switches for scanning, outages and slow pages.</summary>
public sealed class FakeLms : ILmsClient, ILibraryApi, IServerApi
{
    public List<Genre> Genres { get; } = new();

    /// <summary>Extra genre links by genre id: tracks that carry that genre besides their own.</summary>
    public Dictionary<int, List<int>> ExtraGenreTracks { get; } = new();

    /// <summary>Genre-link requests (queries by genre id) received.</summary>
    public int GenreTrackCalls { get; private set; }

    /// <summary>Called with each genre-link query.</summary>
    public Action<TrackQuery>? OnGenreQuery { get; set; }

    public List<Artist> Artists { get; } = new();

    public List<Album> Albums { get; } = new();

    public List<Track> Tracks { get; } = new();

    public string Uuid { get; set; } = "uuid-1";

    public DateTimeOffset? LastScan { get; set; } = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    /// <summary>When set, every server call throws it (an outage).</summary>
    public Exception? Outage { get; set; }

    /// <summary>Number of <c>GetScanStateAsync</c> calls that still answer "scanning".</summary>
    public int ScanPollsRemaining { get; set; }

    /// <summary>Called before each tracks page; lets a test cancel or fail mid-sync.</summary>
    public Func<int, CancellationToken, Task>? OnTracksPage { get; set; }

    public int ArtistCalls { get; private set; }

    public int AlbumCalls { get; private set; }

    public int TrackCalls { get; private set; }

    public List<int> TrackAlbumQueries { get; } = new();

    public TrackQuery? LastBulkTrackQuery { get; private set; }

    public Uri BaseUri { get; } = new("http://fake/");

    public ILmsRpcClient Rpc => throw new NotSupportedException();

    public ILibraryApi Library => this;

    public IPlayerApi Player => throw new NotSupportedException();

    public IQueueApi Queue => throw new NotSupportedException();

    public IServerApi Server => this;

    public static FakeLms WithLibrary(int artists = 3, int albumsPerArtist = 2, int tracksPerAlbum = 4)
    {
        var lms = new FakeLms();
        lms.Genres.Add(new Genre(1, "Metal", null));
        lms.Genres.Add(new Genre(2, "Jazz", null));
        var albumId = 100;
        var trackId = 1000;
        for (var a = 1; a <= artists; a++)
        {
            lms.Artists.Add(new Artist(a, $"Artist {a}", null));
            for (var k = 0; k < albumsPerArtist; k++)
            {
                var id = albumId++;
                lms.Albums.Add(new Album(id, $"Album {id}", $"Artist {a}", a, 2000 + k, $"art{id}", null, false, "ALBUM", 1));
                for (var t = 1; t <= tracksPerAlbum; t++)
                {
                    lms.Tracks.Add(MakeTrack(trackId++, id, t, $"Track {t} of {id}", a % 2 == 0 ? "Jazz" : "Metal"));
                }
            }
        }

        return lms;
    }

    public static Track MakeTrack(int id, int albumId, int number, string title, string? genre = "Metal", string? url = null) =>
        new(id, title, "Artist", 1, "Album", albumId, number, 1, TimeSpan.FromSeconds(200 + number), null, url, genre, null, null, null, null, null, null, null, null);

    public ServerStatus Status() => new(
        "9.1.1",
        Uuid,
        LastScan,
        Albums.Count,
        Artists.Count,
        Genres.Count,
        Tracks.Count,
        TimeSpan.Zero,
        Array.Empty<PlayerInfo>());

    private void ThrowIfDown()
    {
        if (Outage is not null)
        {
            throw Outage;
        }
    }

    private static Page<T> Slice<T>(List<T> all, int start, int count) =>
        new(all.Skip(start).Take(count).ToList(), all.Count, start);

    // IServerApi
    public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult("9.1.1");

    public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDown();
        return Task.FromResult(Status());
    }

    public Task<IReadOnlyList<PlayerInfo>> GetPlayersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PlayerInfo>>(Array.Empty<PlayerInfo>());

    public Task<ScanState> GetScanStateAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDown();
        var scanning = ScanPollsRemaining > 0;
        if (scanning)
        {
            ScanPollsRemaining--;
        }

        return Task.FromResult(new ScanState(scanning, new Dictionary<string, string>()));
    }

    public Task<int> GetTrackCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(Tracks.Count);

    public Task<bool> CanAsync(IReadOnlyList<string> command, CancellationToken cancellationToken = default) => Task.FromResult(true);

    // ILibraryApi
    /// <summary>Search terms the server was asked for (artists, albums, tracks and the global search together).</summary>
    public List<string> SearchedTerms { get; } = new();

    /// <summary>Tags of the last album search (LMS only returns the album title when asked with the album tag).</summary>
    public AlbumTags? LastAlbumSearchTags { get; private set; }

    /// <summary>Called for every server search; lets a test delay or fail it.</summary>
    public Func<string, CancellationToken, Task>? OnSearch { get; set; }

    private static bool WordPrefixes(string term, string name) =>
        term.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w =>
            name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(n => n.StartsWith(w, StringComparison.OrdinalIgnoreCase)));

    private async Task SearchHook(string term, CancellationToken ct)
    {
        ThrowIfDown();
        lock (SearchedTerms)
        {
            SearchedTerms.Add(term);
        }

        if (OnSearch is not null)
        {
            await OnSearch(term, ct);
        }
    }

    public async Task<Page<Artist>> GetArtistsAsync(ArtistQuery? query = null, int start = 0, int count = 5000, CancellationToken cancellationToken = default)
    {
        if (query?.Search is { } term)
        {
            await SearchHook(term, cancellationToken);
            return Slice(Artists.Where(a => WordPrefixes(term, a.Name)).ToList(), start, count);
        }

        ThrowIfDown();
        ArtistCalls++;
        return Slice(Artists, start, count);
    }

    public async Task<Page<Album>> GetAlbumsAsync(AlbumQuery? query = null, int start = 0, int count = 5000, CancellationToken cancellationToken = default)
    {
        if (query?.Search is { } term)
        {
            await SearchHook(term, cancellationToken);
            LastAlbumSearchTags = query.Tags;
            return Slice(Albums.Where(a => WordPrefixes(term, a.Title + " " + a.ArtistName)).ToList(), start, count);
        }

        ThrowIfDown();
        AlbumCalls++;
        return Slice(Albums, start, count);
    }

    public async Task<Page<Track>> GetTracksAsync(TrackQuery query, int start = 0, int count = 500, CancellationToken cancellationToken = default)
    {
        if (query.Search is { } term)
        {
            await SearchHook(term, cancellationToken);
            return Slice(Tracks.Where(t => WordPrefixes(term, t.Title + " " + t.Artist + " " + t.Album)).ToList(), start, count);
        }

        ThrowIfDown();
        if (query.AlbumId is { } albumId)
        {
            TrackAlbumQueries.Add(albumId);
            return Slice(Tracks.Where(t => t.AlbumId == albumId).ToList(), start, count);
        }

        if (query.GenreId is { } genreId)
        {
            // The sync's genre-link pass: every track of the genre (a track's own genre, plus the extra links a test adds).
            GenreTrackCalls++;
            OnGenreQuery?.Invoke(query);
            var name = Genres.FirstOrDefault(g => g.Id == genreId)?.Name;
            var extra = ExtraGenreTracks.GetValueOrDefault(genreId) ?? new List<int>();
            return Slice(Tracks.Where(t => (name is not null && t.Genre == name) || extra.Contains(t.Id)).ToList(), start, count);
        }

        TrackCalls++;
        LastBulkTrackQuery = query;
        if (query.Tags.HasFlag(TrackTags.Genre) && query.Sort != TrackSort.AlbumTrack)
        {
            // What LMS 9.1.1 does: it drops the connection (see CatalogService.FetchTracksAsync).
            throw new LmsProtocolException("closed the connection (genre tag needs sort:albumtrack)");
        }

        if (OnTracksPage is not null)
        {
            await OnTracksPage(start, cancellationToken);
        }

        return Slice(Tracks, start, count);
    }

    public Task<Page<Genre>> GetGenresAsync(int start = 0, int count = 5000, CancellationToken cancellationToken = default)
    {
        ThrowIfDown();
        return Task.FromResult(Slice(Genres, start, count));
    }

    public async Task<SearchResults> SearchAsync(string term, int count = 10, CancellationToken cancellationToken = default)
    {
        await SearchHook(term, cancellationToken);
        var genres = Genres.Where(g => WordPrefixes(term, g.Name)).Select(g => new SearchHit(g.Id, g.Name)).ToList();
        return new SearchResults(Array.Empty<SearchHit>(), Array.Empty<SearchHit>(), Array.Empty<SearchHit>(), genres, 0, 0, 0, genres.Count);
    }

    public Task<Track?> GetTrackAsync(int trackId, TrackTags tags = TrackTags.All, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Track?> GetTrackByUrlAsync(string url, TrackTags tags = TrackTags.All, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Page<SavedPlaylist>> GetPlaylistsAsync(int start = 0, int count = 500, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
