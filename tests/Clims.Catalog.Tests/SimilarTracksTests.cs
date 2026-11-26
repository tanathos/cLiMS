using Clims.Catalog;

namespace Clims.Catalog.Tests;

public class SimilarTracksTests
{
    /// <summary>One track to put in a test library; each album of a library is given by the id and year its tracks name.</summary>
    private sealed record Song(int Id, int Album, int Year, string Artist, params string[] Genres);

    private static Catalog Library(params Song[] songs)
    {
        var names = songs.SelectMany(s => s.Genres).Distinct().ToList();
        var genres = names.Select((n, i) => new CatalogGenre(i + 1, n)).ToList();
        var albums = songs.GroupBy(s => s.Album)
            .Select(g => new CatalogAlbum(g.Key, $"A{g.Key}", null, g.First().Artist, g.First().Year, null, false, 1));
        var tracks = songs.Select(s => new CatalogTrack(s.Id, s.Album, $"T{s.Id}", s.Artist, null, 1, s.Id, TimeSpan.FromMinutes(3), null));
        var links = genres.ToDictionary(g => g.Id, g => songs.Where(s => s.Genres.Contains(g.Name)).Select(s => s.Id).ToArray());
        return new Catalog(genres, Array.Empty<CatalogArtist>(), albums, tracks, null, genreTracks: links);
    }

    /// <summary>A track of its own album and artist.</summary>
    private static Song Single(int id, int year, params string[] genres) => new(id, id, year, $"Artist {id}", genres);

    private static int[] Pick(Catalog catalog, int seedAlbum, int count, int[]? exclude = null, int[]? seed = null)
    {
        var chosen = catalog.TracksOf(seedAlbum).Where(t => seed is null || seed.Contains(t.Id)).ToList();
        return SimilarTracks.Pick(catalog, catalog.Album(seedAlbum)!.Year, chosen, count, exclude ?? [], new Random(1))
            .Select(x => x.Track.Id)
            .Order()
            .ToArray();
    }

    [Fact]
    public void A_better_genre_match_comes_before_a_closer_year()
    {
        var catalog = Library(
            Single(1, 1990, "Rock", "Metal", "Doom"),
            Single(2, 1960, "Rock", "Metal", "Doom"),
            Single(3, 1990, "Rock", "Metal"),
            Single(4, 1990, "Rock"),
            Single(5, 1990, "Jazz"));

        Assert.Equal(new[] { 2 }, Pick(catalog, 1, 1));
        Assert.Equal(new[] { 2, 3 }, Pick(catalog, 1, 2));
        Assert.Equal(new[] { 2, 3, 4 }, Pick(catalog, 1, 10));
    }

    [Fact]
    public void Within_a_genre_match_the_closer_year_comes_first_and_unknown_years_last()
    {
        var catalog = Library(
            Single(1, 1990, "Rock"),
            Single(2, 0, "Rock"),
            Single(3, 2020, "Rock"),
            Single(4, 1999, "Rock"),
            Single(5, 1994, "Rock"),
            Single(6, 1988, "Rock"),
            Single(7, 1990, "Rock"));

        Assert.Equal(new[] { 7 }, Pick(catalog, 1, 1));
        Assert.Equal(new[] { 6, 7 }, Pick(catalog, 1, 2));
        Assert.Equal(new[] { 5, 6, 7 }, Pick(catalog, 1, 3));
        Assert.Equal(new[] { 4, 5, 6, 7 }, Pick(catalog, 1, 4));
        Assert.Equal(new[] { 2, 3, 4, 5, 6, 7 }, Pick(catalog, 1, 6));
    }

    [Fact]
    public void A_genre_of_one_stray_track_of_an_album_is_not_required()
    {
        var songs = Enumerable.Range(1, 11).Select(i => new Song(i, 1, 1990, "Seed", i == 11 ? ["Rock", "Polka"] : ["Rock"])).ToList();
        songs.Add(Single(20, 1990, "Rock"));
        songs.Add(Single(21, 1990, "Polka"));
        var catalog = Library([.. songs]);

        Assert.Equal(new[] { 20 }, Pick(catalog, 1, 1));
        Assert.Equal(new[] { 20, 21 }, Pick(catalog, 1, 5));
    }

    [Fact]
    public void The_selection_and_the_queued_tracks_are_left_out()
    {
        var catalog = Library(
            new Song(1, 1, 1990, "A", "Rock"), new Song(2, 1, 1990, "A", "Rock"), Single(3, 1990, "Rock"), Single(4, 1990, "Rock"));

        Assert.Equal(new[] { 3 }, Pick(catalog, 1, 10, exclude: [4]));
        Assert.Equal(new[] { 2, 3, 4 }, Pick(catalog, 1, 10, seed: [1]));
    }

    [Fact]
    public void Albums_and_artists_are_limited_while_others_can_fill_the_list()
    {
        var songs = new List<Song> { Single(1, 1990, "Rock") };
        songs.AddRange(Enumerable.Range(10, 5).Select(i => new Song(i, 2, 1990, "One album", "Rock")));
        songs.AddRange(Enumerable.Range(20, 6).Select(i => new Song(i, i, 1990, "Prolific", "Rock")));
        songs.AddRange(Enumerable.Range(30, 4).Select(i => Single(i, 1950, "Rock")));
        var catalog = Library([.. songs]);

        var picked = Pick(catalog, 1, 10);

        Assert.Equal(SimilarTracks.PerAlbum, picked.Count(id => id is >= 10 and < 20));
        Assert.Equal(SimilarTracks.PerArtist, picked.Count(id => id is >= 20 and < 30));
        Assert.Equal(4, picked.Count(id => id >= 30));
    }

    [Fact]
    public void The_limits_give_way_when_the_list_would_be_short()
    {
        var songs = new List<Song> { Single(1, 1990, "Rock") };
        songs.AddRange(Enumerable.Range(10, 8).Select(i => new Song(i, 2, 1990, "One album", "Rock")));
        var catalog = Library([.. songs]);

        Assert.Equal(6, Pick(catalog, 1, 6).Length);
        Assert.Equal(8, Pick(catalog, 1, 25).Length);
    }

    [Fact]
    public void The_same_artist_does_not_follow_itself()
    {
        var songs = new List<Song> { Single(1, 1990, "Rock") };
        songs.AddRange(Enumerable.Range(10, 2).Select(i => new Song(i, i, 1990, "Twice", "Rock")));
        songs.AddRange(Enumerable.Range(20, 8).Select(i => Single(i, 1990, "Rock")));
        var catalog = Library([.. songs]);

        for (var seed = 0; seed < 50; seed++)
        {
            var picked = SimilarTracks.Pick(catalog, 1990, catalog.TracksOf(1), 10, [], new Random(seed));

            // Only a pair left at the very end could not be kept apart.
            Assert.All(Enumerable.Range(1, picked.Count - 2), i => Assert.NotEqual(picked[i - 1].Track.Artist, picked[i].Track.Artist));
        }
    }

    [Fact]
    public void A_selection_without_a_genre_has_nothing_similar()
    {
        var catalog = Library(Single(1, 1990), Single(2, 1990, "Rock"));

        Assert.False(SimilarTracks.HasGenre(catalog, catalog.TracksOf(1)));
        Assert.Empty(Pick(catalog, 1, 10));
    }
}
