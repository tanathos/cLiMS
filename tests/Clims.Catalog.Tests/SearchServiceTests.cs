using System.Diagnostics;
using Clims.Lms;

namespace Clims.Catalog.Tests;

public class SearchServiceTests
{
    private static readonly SearchServiceOptions Fast = new() { ServerDebounce = TimeSpan.FromMilliseconds(20) };

    private static async Task WaitAsync(Func<bool> condition)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "timed out");
            await Task.Delay(5);
        }
    }

    private sealed class Collector
    {
        private readonly List<SearchOutcome> _all = new();

        public Collector(ISearchService service) => service.Changed += o =>
        {
            lock (_all)
            {
                _all.Add(o);
            }
        };

        public List<SearchOutcome> All
        {
            get
            {
                lock (_all)
                {
                    return _all.ToList();
                }
            }
        }

        public SearchOutcome Last => All[^1];
    }

    private static FakeLms Library()
    {
        var lms = FakeLms.WithLibrary(artists: 3, albumsPerArtist: 2, tracksPerAlbum: 3);
        return lms;
    }

    [Fact]
    public async Task A_complete_index_answers_alone_and_never_asks_the_server()
    {
        var lms = Library();
        var catalog = Build.SyncedCatalog(lms);
        using var service = new SearchService(() => catalog, lms, Fast);
        var seen = new Collector(service);

        service.Query("artist 2");
        await Task.Delay(100);

        var only = Assert.Single(seen.All);
        Assert.Equal(SearchGroups.None, only.ServerGroups);
        Assert.False(only.ServerPending);
        Assert.Equal(1, only.Result.Artists.Total);
        Assert.Empty(lms.SearchedTerms);
    }

    [Fact]
    public async Task Without_an_index_everything_comes_from_the_server()
    {
        var lms = Library();
        using var service = new SearchService(() => null, lms, Fast);
        var seen = new Collector(service);

        service.Query("artist 2");
        Assert.True(seen.Last.ServerPending);          // local answer first: empty, with the server on its way
        Assert.True(seen.Last.Result.IsEmpty);
        Assert.Equal(SearchGroups.All, seen.Last.ServerGroups);

        await WaitAsync(() => !seen.Last.ServerPending);
        var done = seen.Last;
        Assert.Equal("Artist 2", Assert.Single(done.Result.Artists.Items).Name);
        Assert.Contains(done.Result.Albums.Items, a => a.ArtistName == "Artist 2");
        Assert.NotEmpty(done.Result.Tracks.Items);
    }

    [Fact]
    public async Task Server_results_keep_the_query_words_for_highlighting_and_bring_album_details()
    {
        var lms = Library();
        using var service = new SearchService(() => null, lms, Fast);
        var seen = new Collector(service);

        service.Query("artist 2");
        Assert.Equal(new[] { "artist", "2" }, seen.Last.Result.Words);   // already in the immediate, empty answer

        await WaitAsync(() => !seen.Last.ServerPending);
        var done = seen.Last.Result;
        Assert.Equal(new[] { "artist", "2" }, done.Words);
        Assert.True(lms.LastAlbumSearchTags!.Value.HasFlag(AlbumTags.Album));   // without it LMS sends no album titles
        var track = done.Tracks.Items[0];
        Assert.Equal("Album", done.TrackExtras![track.Id].AlbumTitle);
    }

    [Fact]
    public async Task While_tracks_load_artists_and_albums_are_local_and_tracks_come_from_the_server()
    {
        var lms = Library();
        var full = Build.SyncedCatalog(lms);
        var partial = new Catalog(full.Genres, full.Artists, full.Albums, Array.Empty<CatalogTrack>(), null, tracksLoaded: false);
        using var service = new SearchService(() => partial, lms, Fast);
        var seen = new Collector(service);

        service.Query("track 1");
        Assert.Equal(SearchGroups.Tracks, seen.Last.ServerGroups);
        Assert.True(seen.Last.ServerPending);

        await WaitAsync(() => !seen.Last.ServerPending);
        Assert.NotEmpty(seen.Last.Result.Tracks.Items);
        Assert.Equal(full.Search("track 1").Albums.Total, seen.Last.Result.Albums.Total); // local group untouched
    }

    [Fact]
    public async Task Only_the_last_of_several_quick_keystrokes_reaches_the_server()
    {
        var lms = Library();
        using var service = new SearchService(() => null, lms, new SearchServiceOptions { ServerDebounce = TimeSpan.FromMilliseconds(80) });
        var seen = new Collector(service);

        service.Query("a");
        service.Query("ar");
        service.Query("art");
        await WaitAsync(() => !seen.Last.ServerPending);

        Assert.Equal("art", seen.Last.Query);
        lock (lms.SearchedTerms)
        {
            Assert.Equal(new[] { "art" }, lms.SearchedTerms.Distinct());
        }
    }

    [Fact]
    public async Task A_late_answer_for_older_text_is_dropped()
    {
        var lms = Library();
        var release = new TaskCompletionSource();
        lms.OnSearch = async (term, ct) =>
        {
            if (term == "art")
            {
                await release.Task; // the first search hangs; the second overtakes it
            }
        };
        using var service = new SearchService(() => null, lms, Fast);
        var seen = new Collector(service);

        service.Query("art");
        await WaitAsync(() => { lock (lms.SearchedTerms) { return lms.SearchedTerms.Contains("art"); } });
        service.Query("artist 3");
        await WaitAsync(() => seen.All.Any(o => o.Query == "artist 3" && !o.ServerPending));

        release.SetResult();
        await Task.Delay(100);

        Assert.DoesNotContain(seen.All, o => o.Query == "art" && !o.ServerPending);
        Assert.Equal("artist 3", seen.Last.Query);
    }

    [Fact]
    public async Task An_unreachable_server_keeps_the_local_result_and_adds_a_note()
    {
        var lms = Library();
        var full = Build.SyncedCatalog(lms);
        var partial = new Catalog(full.Genres, full.Artists, full.Albums, Array.Empty<CatalogTrack>(), null, tracksLoaded: false);
        lms.Outage = new LmsConnectionException("down");
        using var service = new SearchService(() => partial, lms, Fast);
        var seen = new Collector(service);

        service.Query("artist");
        await WaitAsync(() => !seen.Last.ServerPending);

        Assert.NotNull(seen.Last.Note);
        Assert.Equal(3, seen.Last.Result.Artists.Total);   // local artists are still shown
        Assert.Empty(seen.Last.Result.Tracks.Items);
    }

    [Fact]
    public async Task Without_a_server_only_the_index_answers()
    {
        var full = Build.SyncedCatalog(Library());
        var partial = new Catalog(full.Genres, full.Artists, full.Albums, Array.Empty<CatalogTrack>(), null, tracksLoaded: false);
        using var service = new SearchService(() => partial, null, Fast);
        var seen = new Collector(service);

        service.Query("artist");
        await Task.Delay(80);

        var only = Assert.Single(seen.All);
        Assert.False(only.ServerPending);
        Assert.Equal(3, only.Result.Artists.Total);
    }

    [Fact]
    public async Task An_empty_query_clears_the_result_without_asking_the_server()
    {
        var lms = Library();
        using var service = new SearchService(() => null, lms, Fast);
        var seen = new Collector(service);

        service.Query("   ");
        await Task.Delay(80);

        var only = Assert.Single(seen.All);
        Assert.True(only.Result.IsEmpty);
        Assert.False(only.ServerPending);
        Assert.Empty(lms.SearchedTerms);
    }

    [Fact]
    public async Task Genres_are_found_locally_and_through_the_server()
    {
        var lms = Library();
        var catalog = Build.SyncedCatalog(lms);
        using var local = new SearchService(() => catalog, lms, Fast);
        var seen = new Collector(local);
        local.Query("jaz");
        Assert.Equal("Jazz", Assert.Single(seen.Last.Result.Genres.Items).Name);

        using var remote = new SearchService(() => null, lms, Fast);
        var remoteSeen = new Collector(remote);
        remote.Query("jaz");
        await WaitAsync(() => !remoteSeen.Last.ServerPending);
        Assert.Equal("Jazz", Assert.Single(remoteSeen.Last.Result.Genres.Items).Name);
    }
}
