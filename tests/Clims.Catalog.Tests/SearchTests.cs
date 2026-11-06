using System.Diagnostics;
using Clims.Lms;

namespace Clims.Catalog.Tests;

public class RankingTests
{
    private static Catalog WithArtists(params string[] names) => new(
        Array.Empty<CatalogGenre>(),
        names.Select((n, i) => new CatalogArtist(i + 1, n)),
        Array.Empty<CatalogAlbum>(),
        Array.Empty<CatalogTrack>(),
        null);

    [Fact]
    public void Exact_beats_prefix_beats_word_start_beats_inside()
    {
        var c = WithArtists("Environment", "Cast Iron", "Iron Maiden", "Iron");
        Assert.Equal(new[] { "Iron", "Iron Maiden", "Cast Iron", "Environment" }, c.Search("iron").Artists.Items.Select(a => a.Name));
    }

    [Fact]
    public void Ties_go_to_the_shorter_name_then_alphabetical()
    {
        var c = WithArtists("Iron Longer Name", "Iron B", "Iron A");
        Assert.Equal(new[] { "Iron A", "Iron B", "Iron Longer Name" }, c.Search("iron").Artists.Items.Select(a => a.Name));
    }

    [Fact]
    public void Multi_word_queries_match_in_any_order_and_rank_by_how_well()
    {
        var c = WithArtists("Maiden Iron Tribute", "Iron Maiden", "Ironmaiden Fan Club");
        var names = c.Search("maiden iron").Artists.Items.Select(a => a.Name).ToList();
        Assert.Equal("Maiden Iron Tribute", names[0]);  // starts with the query text, in the order typed
        Assert.Equal("Iron Maiden", names[1]);          // every word starts a word, any order
        Assert.Equal("Ironmaiden Fan Club", names[2]);  // "iron" and "maiden" occur inside words: weakest tier
    }

    [Fact]
    public void A_match_only_through_the_artist_ranks_below_a_title_match()
    {
        var c = new Catalog(
            Array.Empty<CatalogGenre>(),
            new[] { new CatalogArtist(1, "Iron Maiden"), new CatalogArtist(2, "Other") },
            new[] { new CatalogAlbum(1, "Powerslave", 1, "Iron Maiden", 1984, null, false, 1), new CatalogAlbum(2, "Iron Sky", 2, "Other", 2012, null, false, 1) },
            Array.Empty<CatalogTrack>(),
            null);
        var albums = c.Search("iron").Albums.Items.Select(a => a.Title);
        Assert.Equal(new[] { "Iron Sky", "Powerslave" }, albums);
    }

    [Fact]
    public void A_title_match_by_the_matching_artist_is_boosted()
    {
        var c = new Catalog(
            Array.Empty<CatalogGenre>(),
            new[] { new CatalogArtist(1, "Iron Maiden"), new CatalogArtist(2, "Iron") },
            new[] { new CatalogAlbum(1, "A", 1, "Iron Maiden", 1984, null, false, 1), new CatalogAlbum(2, "B", 2, "Iron", 1984, null, false, 1) },
            new[]
            {
                new CatalogTrack(1, 1, "Iron Maiden", "Somebody Else", 9, 1, 1, TimeSpan.Zero, null),
                new CatalogTrack(2, 2, "Iron Maiden", "Iron Maiden", 1, 1, 1, TimeSpan.Zero, null),
            },
            null);
        Assert.Equal(new[] { 2, 1 }, c.Search("iron maiden").Tracks.Items.Select(t => t.Id));
    }

    [Fact]
    public void Totals_count_every_match_while_items_are_cut()
    {
        var c = WithArtists(Enumerable.Range(0, 30).Select(i => $"Band {i:00}").ToArray());
        var r = c.Search("band", maxArtists: 5);
        Assert.Equal(5, r.Artists.Items.Count);
        Assert.Equal(30, r.Artists.Total);
    }

    [Fact]
    public void Accents_and_case_are_ignored_both_ways()
    {
        var c = WithArtists("Édith Piaf", "Motörhead");
        Assert.Single(c.Search("EDITH").Artists.Items);
        Assert.Single(c.Search("motorhead").Artists.Items);
        Assert.Single(c.Search("MOTÖR").Artists.Items);
    }

    [Fact]
    public void Punctuation_in_names_and_queries_is_ignored()
    {
        var c = WithArtists("AC/DC", "Guns N' Roses");
        Assert.Single(c.Search("ac dc").Artists.Items);
        Assert.Single(c.Search("ac/dc").Artists.Items);
        Assert.Single(c.Search("guns n roses").Artists.Items);
    }

    [Fact]
    public void Local_search_over_twenty_thousand_tracks_is_fast()
    {
        var rnd = new Random(1);
        string Word() => new string(Enumerable.Range(0, rnd.Next(3, 9)).Select(_ => (char)('a' + rnd.Next(26))).ToArray());
        var albums = Enumerable.Range(1, 2000).Select(i => new CatalogAlbum(i, $"{Word()} {Word()}", 1, "Artist", 2000, null, false, 1)).ToList();
        var tracks = albums.SelectMany(a => Enumerable.Range(1, 10).Select(n =>
            new CatalogTrack(a.Id * 100 + n, a.Id, $"{Word()} {Word()} {Word()}", $"Artist {Word()}", 1, 1, n, TimeSpan.FromMinutes(3), "Rock"))).ToList();
        var c = new Catalog(Array.Empty<CatalogGenre>(), new[] { new CatalogArtist(1, "Artist") }, albums, tracks, null);
        Assert.Equal(20_000, c.TrackCount);

        _ = c.Search("e"); // builds the track index
        foreach (var query in new[] { "e", "a", "th", "abc def", "zzzz" })
        {
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < 10; i++)
            {
                _ = c.Search(query);
            }

            Assert.True(sw.Elapsed.TotalMilliseconds / 10 < 50, $"'{query}' took {sw.Elapsed.TotalMilliseconds / 10:F1} ms");
        }
    }
}

public class HighlightTests
{
    [Fact]
    public void Marks_the_matching_fragment_of_the_original_text()
    {
        var h = TextFolding.Highlights("Pantéon Blues", new[] { "pante" });
        Assert.Equal(new[] { (0, 5) }, h);
    }

    [Fact]
    public void Marks_each_word_wherever_it_is_and_merges_touching_ranges()
    {
        Assert.Equal(new[] { (0, 4), (14, 3) }, TextFolding.Highlights("Iron Maiden - Run", new[] { "iron", "run" }));
        Assert.Equal(new[] { (0, 4) }, TextFolding.Highlights("Iron", new[] { "ir", "iron" })); // overlapping: one range
    }

    [Fact]
    public void Prefers_a_word_start_over_a_hit_inside_a_word()
    {
        var h = TextFolding.Highlights("Aurora Rise", new[] { "r" });
        Assert.Equal((7, 1), h[0]); // "Rise", not the r inside "Aurora"
    }

    [Fact]
    public void Nothing_to_mark_gives_no_ranges()
    {
        Assert.Empty(TextFolding.Highlights("Wish", new[] { "zzz" }));
        Assert.Empty(TextFolding.Highlights("", new[] { "a" }));
        Assert.Empty(TextFolding.Highlights("Wish", Array.Empty<string>()));
    }

    [Fact]
    public void Accents_do_not_shift_the_ranges()
    {
        Assert.Equal(new[] { (6, 4) }, TextFolding.Highlights("Édith Piaf", new[] { "piaf" }));
        Assert.Equal(new[] { (0, 3) }, TextFolding.Highlights("Édith Piaf", new[] { "edi" }));
    }
}

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
