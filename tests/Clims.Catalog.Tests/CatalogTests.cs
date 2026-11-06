using Clims.Lms;

namespace Clims.Catalog.Tests;

public class TextTests
{
    [Theory]
    [InlineData("Mew", "M")]
    [InlineData("The 13th Floor Elevators", "#")]
    [InlineData("The Cure", "C")]
    [InlineData("the cure", "C")]
    [InlineData("Édith Piaf", "E")]
    [InlineData("  ", "#")]
    [InlineData("4 Non Blondes", "#")]
    [InlineData("¿Qué?", "#")]
    public void Text_key(string name, string expected) => Assert.Equal(expected, new CatalogArtist(1, name).TextKey);

    [Fact]
    public void Folding_removes_case_and_accents() => Assert.Equal("pante", TextFolding.Fold("PANTÉ"));

    [Fact]
    public void Words_split_on_punctuation() =>
        Assert.Equal(new[] { "ac", "dc", "back", "in", "black" }, TextFolding.Words("ac/dc - back in black"));
}

public class CatalogModelTests
{
    private static Catalog Sample()
    {
        var artists = new[] { new CatalogArtist(1, "The Cure"), new CatalogArtist(2, "Mew"), new CatalogArtist(3, "Édith Piaf") };
        var albums = new[]
        {
            new CatalogAlbum(10, "Wish", 1, "The Cure", 1992, "a", false, 1),
            new CatalogAlbum(11, "Disintegration", 1, "The Cure", 1989, null, false, 1),
            new CatalogAlbum(12, "Unknown Year", 1, "The Cure", 0, null, false, 1),
            new CatalogAlbum(20, "+-", 2, "Mew", 2015, "b", false, 1),
            new CatalogAlbum(30, "La Vie en rose", 3, "Édith Piaf", 1950, null, false, 1),
            new CatalogAlbum(40, "Orphan", null, "Nobody", 2001, null, false, 1),
        };
        var tracks = new[]
        {
            new CatalogTrack(1, 10, "Open", "The Cure", 1, 1, 1, TimeSpan.FromMinutes(5), "Rock"),
            new CatalogTrack(2, 10, "High", "The Cure", 1, 1, 2, TimeSpan.FromMinutes(3), "Rock"),
            new CatalogTrack(3, 10, "Wendy Time", "The Cure", 1, 1, 3, TimeSpan.FromMinutes(2), "Pop"),
            new CatalogTrack(4, 11, "Plainsong", "The Cure", 1, 1, 1, TimeSpan.FromMinutes(5), "Rock"),
            new CatalogTrack(5, 20, "Satellites", "Mew", 2, 1, 1, TimeSpan.FromMinutes(6), "Alternative"),
            new CatalogTrack(6, 30, "Pantéon Blues", "Édith Piaf", 3, 1, 1, TimeSpan.FromMinutes(3), null),
        };
        return new Catalog(Array.Empty<CatalogGenre>(), artists, albums, tracks, null);
    }

    [Fact]
    public void An_artists_albums_are_oldest_first_with_unknown_year_last()
    {
        var titles = Sample().AlbumsOfArtist(1).Select(a => a.Title);
        Assert.Equal(new[] { "Disintegration", "Wish", "Unknown Year" }, titles);
    }

    [Fact]
    public void Album_counts_and_unknown_artists()
    {
        var c = Sample();
        Assert.Equal(3, c.AlbumCountOf(1));
        Assert.Equal(0, c.AlbumCountOf(999));
        Assert.Empty(c.AlbumsOfArtist(999));
    }

    [Fact]
    public void All_albums_follow_artist_order_and_orphans_come_last()
    {
        var ids = Sample().Albums.Select(a => a.Id);
        Assert.Equal(new[] { 11, 10, 12, 20, 30, 40 }, ids);
    }

    [Fact]
    public void Tracks_are_ordered_by_disc_then_number()
    {
        var c = new Catalog(
            Array.Empty<CatalogGenre>(),
            new[] { new CatalogArtist(1, "A") },
            new[] { new CatalogAlbum(1, "X", 1, "A", 2000, null, false, 2) },
            new[]
            {
                new CatalogTrack(3, 1, "d2t1", null, null, 2, 1, TimeSpan.Zero, null),
                new CatalogTrack(2, 1, "d1t2", null, null, 1, 2, TimeSpan.Zero, null),
                new CatalogTrack(1, 1, "d1t1", null, null, 1, 1, TimeSpan.Zero, null),
            },
            null);
        Assert.Equal(new[] { "d1t1", "d1t2", "d2t1" }, c.TracksOf(1).Select(t => t.Title));
    }

    [Fact]
    public void Summary_has_count_duration_and_genres_by_frequency()
    {
        var s = Sample().Summary(10);
        Assert.Equal(3, s.TrackCount);
        Assert.Equal(TimeSpan.FromMinutes(10), s.Duration);
        Assert.Equal(new[] { "Rock", "Pop" }, s.Genres);
    }

    [Fact]
    public void Summary_of_an_album_without_tracks_is_empty()
    {
        var s = Sample().Summary(12);
        Assert.Equal(0, s.TrackCount);
        Assert.Equal(TimeSpan.Zero, s.Duration);
        Assert.Empty(s.Genres);
    }

    [Fact]
    public void Genres_find_albums_from_their_tracks()
    {
        var c = Sample();
        Assert.Equal(new[] { 11, 10 }, c.AlbumsOfGenre("Rock").Select(a => a.Id));
        Assert.Equal(new[] { 10 }, c.AlbumsOfGenre("pop").Select(a => a.Id)); // case-insensitive
        Assert.Equal(1, c.AlbumCountOfGenre("Alternative"));
        Assert.Empty(c.AlbumsOfGenre("Polka"));
    }

    [Fact]
    public void Years_are_newest_first_with_unknown_last()
    {
        var c = Sample();
        Assert.Equal(new[] { 2015, 2001, 1992, 1989, 1950, 0 }, c.Years);
        Assert.Equal(new[] { 12 }, c.AlbumsOfYear(0).Select(a => a.Id));
        Assert.Equal(new[] { 20 }, c.AlbumsOfYear(2015).Select(a => a.Id));
    }

    [Fact]
    public void Replacing_tracks_reports_whether_anything_changed()
    {
        var c = Sample();
        var same = c.TracksOf(11).ToList();
        var versionBefore = c.Version;

        Assert.False(c.ReplaceAlbumTracks(11, same));
        Assert.Equal(versionBefore, c.Version);

        var changed = same.Concat(new[] { new CatalogTrack(99, 11, "Lovesong", "The Cure", 1, 1, 2, TimeSpan.FromMinutes(3), "Pop") }).ToList();
        Assert.True(c.ReplaceAlbumTracks(11, changed));
        Assert.Equal(versionBefore + 1, c.Version);
        Assert.Equal(2, c.Summary(11).TrackCount);                                   // summary cache invalidated
        Assert.Contains(c.AlbumsOfGenre("Pop"), a => a.Id == 11);                    // genre index invalidated
        Assert.Contains(c.Search("lovesong").Tracks.Items, t => t.Id == 99);         // search index updated
    }

    [Fact]
    public void Replacing_tracks_of_an_unknown_album_is_ignored() =>
        Assert.False(Sample().ReplaceAlbumTracks(12345, Array.Empty<CatalogTrack>()));

    [Fact]
    public void An_album_can_lose_all_its_tracks()
    {
        var c = Sample();
        Assert.True(c.ReplaceAlbumTracks(10, Array.Empty<CatalogTrack>()));
        Assert.Empty(c.TracksOf(10));
    }

    [Theory]
    [InlineData("cure", 1, 0)]                  // artist word prefix
    [InlineData("the cu", 1, 0)]                // multi word
    [InlineData("cure the", 1, 0)]              // any order
    [InlineData("edith", 1, 0)]                 // accent folded
    [InlineData("PIAF", 1, 0)]                  // case folded
    [InlineData("pante", 0, 1)]                 // track with accent, found without it
    [InlineData("ure", 1, 0)]                   // inside a word: the server cannot do this, the local index can
    [InlineData("zzz", 0, 0)]
    [InlineData("   ", 0, 0)]
    public void Search_matches_anywhere_in_any_order(string text, int artists, int tracksAtLeast)
    {
        var r = Sample().Search(text);
        Assert.Equal(artists, r.Artists.Total);
        Assert.True(r.Tracks.Total >= tracksAtLeast);
    }

    [Fact]
    public void Search_finds_albums_by_title_and_artist_words()
    {
        var r = Sample().Search("wish cure");
        Assert.Equal(new[] { 10 }, r.Albums.Items.Select(a => a.Id));
    }

    [Fact]
    public void Search_finds_tracks_by_album_title_and_genres_by_name()
    {
        var c = new Catalog(
            new[] { new CatalogGenre(1, "Black Metal") },
            new[] { new CatalogArtist(1, "Immortal") },
            new[] { new CatalogAlbum(1, "Sons of Northern Darkness", 1, "Immortal", 2002, null, false, 1) },
            new[] { new CatalogTrack(1, 1, "One by One", "Immortal", 1, 1, 1, TimeSpan.Zero, "Black Metal") },
            null);
        Assert.Equal(new[] { 1 }, c.Search("one northern").Tracks.Items.Select(t => t.Id));
        Assert.Equal(new[] { 1 }, c.Search("metal").Genres.Items.Select(g => g.Id));
    }

    [Fact]
    public void Search_respects_limits_and_reports_totals()
    {
        var r = Sample().Search("e", maxArtists: 1, maxAlbums: 1, maxTracks: 1);
        Assert.True(r.Artists.Items.Count <= 1 && r.Albums.Items.Count <= 1 && r.Tracks.Items.Count <= 1);
        Assert.True(r.Tracks.Total > 1);
    }

    [Fact]
    public void The_empty_catalogue_is_usable()
    {
        Assert.Empty(Catalog.Empty.Artists);
        Assert.False(Catalog.Empty.TracksLoaded);
        Assert.Empty(Catalog.Empty.Search("x").Artists.Items);
        Assert.Equal(0, Catalog.Empty.TrackCount);
    }

    [Fact]
    public async Task Concurrent_reads_and_replacements_do_not_corrupt_the_model()
    {
        var c = Sample();
        var tracks = c.TracksOf(10).ToList();
        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                c.ReplaceAlbumTracks(10, i % 2 == 0 ? tracks : tracks.Take(1).ToList());
            }
        });
        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                _ = c.Summary(10);
                _ = c.Search("open");
                _ = c.AlbumsOfGenre("Rock");
                _ = c.TrackCount;
            }
        });
        await Task.WhenAll(writer, reader);
    }
}

public class AssessorTests
{
    private static ServerStatus Server(long? scan = 100, int tracks = 10, int albums = 5, int artists = 3, int genres = 2) =>
        new("9", "u", scan is { } s ? DateTimeOffset.FromUnixTimeSeconds(s) : null, albums, artists, genres, tracks, TimeSpan.Zero, Array.Empty<PlayerInfo>());

    private static CatalogMeta Meta(long? scan = 100, int tracks = 10, int albums = 5, int artists = 3, int genres = 2) =>
        new("u", scan, tracks, albums, artists, genres, DateTimeOffset.UnixEpoch);

    [Fact]
    public void No_meta_means_no_index() => Assert.Equal(AssessmentKind.NoIndex, SyncAssessor.Assess(null, Server()).Kind);

    [Fact]
    public void Same_scan_and_counts_is_up_to_date() => Assert.Equal(AssessmentKind.UpToDate, SyncAssessor.Assess(Meta(), Server()).Kind);

    [Fact]
    public void A_moved_lastscan_is_a_change()
    {
        var a = SyncAssessor.Assess(Meta(scan: 100), Server(scan: 200));
        Assert.Equal(AssessmentKind.Changed, a.Kind);
        Assert.Contains("rescanned", a.Reason);
    }

    [Theory]
    [InlineData(11, 5, 3, 2, "tracks 10 to 11")]
    [InlineData(10, 6, 3, 2, "albums 5 to 6")]
    [InlineData(10, 5, 4, 2, "artists 3 to 4")]
    [InlineData(10, 5, 3, 3, "genres 2 to 3")]
    public void Count_changes_are_reported_with_numbers(int tracks, int albums, int artists, int genres, string expected)
    {
        var a = SyncAssessor.Assess(Meta(), Server(tracks: tracks, albums: albums, artists: artists, genres: genres));
        Assert.Equal(AssessmentKind.Changed, a.Kind);
        Assert.Contains(expected, a.Reason);
    }

    [Fact]
    public void Several_changes_are_all_listed()
    {
        var a = SyncAssessor.Assess(Meta(), Server(scan: 5, tracks: 99));
        Assert.Contains("rescanned", a.Reason);
        Assert.Contains("tracks 10 to 99", a.Reason);
    }

    [Fact]
    public void A_server_that_never_scanned_matches_an_index_that_never_saw_a_scan() =>
        Assert.Equal(AssessmentKind.UpToDate, SyncAssessor.Assess(Meta(scan: null), Server(scan: null)).Kind);
}
