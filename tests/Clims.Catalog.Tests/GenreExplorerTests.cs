using Clims.Catalog;

namespace Clims.Catalog.Tests;

public class GenreExplorerTests
{
    private const int Rock = 1;
    private const int Metal = 2;
    private const int Thrash = 3;
    private const int Jazz = 4;

    private static CatalogTrack T(int id, int album, string? artist, string? genre = null) =>
        new(id, album, $"T{id}", artist, null, 1, id, TimeSpan.FromMinutes(3), genre);

    /// <summary>
    /// 1: Rock+Metal+Thrash (Anthrax), 2: Rock+Metal (Anthrax), 3: Rock (Beatles), 4: Metal (Metallica),
    /// 5: Jazz (Miles), 6: Rock+Jazz (Beatles).
    /// </summary>
    private static Catalog Library(bool withLinks = true)
    {
        var genres = new[] { new CatalogGenre(Rock, "Rock"), new CatalogGenre(Metal, "Metal"), new CatalogGenre(Thrash, "Thrash"), new CatalogGenre(Jazz, "Jazz") };
        var albums = new[] { new CatalogAlbum(10, "A", null, "Anthrax", 1990, null, false, 1), new CatalogAlbum(11, "B", null, "Various", 1970, null, true, 1) };
        var tracks = new[]
        {
            T(1, 10, "Anthrax", "Thrash"), T(2, 10, "Anthrax", "Metal"), T(3, 11, "Beatles", "Rock"),
            T(4, 11, "Metallica", "Metal"), T(5, 11, "Miles", "Jazz"), T(6, 11, "Beatles", "Rock"),
        };
        var links = new Dictionary<int, int[]>
        {
            [Rock] = new[] { 1, 2, 3, 6 },
            [Metal] = new[] { 1, 2, 4 },
            [Thrash] = new[] { 1 },
            [Jazz] = new[] { 5, 6 },
        };
        return new Catalog(genres, Array.Empty<CatalogArtist>(), albums, tracks, null, genreTracks: withLinks ? links : null);
    }

    [Fact]
    public void The_first_level_lists_every_genre_alphabetically_with_its_track_count()
    {
        var options = GenreExplorer.Build(Library()).Options(Array.Empty<int>());

        Assert.Equal(new[] { "Jazz", "Metal", "Rock", "Thrash" }, options.Select(o => o.Name));
        Assert.Equal(new[] { 2, 3, 4, 1 }, options.Select(o => o.Tracks));
    }

    [Fact]
    public void A_genre_offers_the_genres_that_share_a_track_with_it_most_tracks_first()
    {
        var options = GenreExplorer.Build(Library()).Options(new[] { Rock });

        Assert.Equal(new[] { "Metal", "Jazz", "Thrash" }, options.Select(o => o.Name));
        Assert.Equal(new[] { 2, 1, 1 }, options.Select(o => o.Tracks));
    }

    [Fact]
    public void Each_step_narrows_to_tracks_tagged_with_the_whole_chain()
    {
        var explorer = GenreExplorer.Build(Library());

        Assert.Equal(new[] { 1, 2 }, explorer.Match(new[] { Rock, Metal }).Select(t => t.Id).Order());
        Assert.Equal(new[] { 1 }, explorer.Match(new[] { Rock, Metal, Thrash }).Select(t => t.Id));
        Assert.Equal(new[] { "Thrash" }, explorer.Options(new[] { Rock, Metal }).Select(o => o.Name));
        Assert.Empty(explorer.Options(new[] { Rock, Metal, Thrash }));
    }

    [Fact]
    public void The_order_of_the_chain_does_not_change_what_matches()
    {
        var explorer = GenreExplorer.Build(Library());

        Assert.Equal(
            explorer.Match(new[] { Rock, Metal }).Select(t => t.Id).Order(),
            explorer.Match(new[] { Metal, Rock }).Select(t => t.Id).Order());
    }

    [Fact]
    public void Genres_that_never_meet_have_no_tracks_in_common()
    {
        var explorer = GenreExplorer.Build(Library());

        Assert.Empty(explorer.Match(new[] { Metal, Jazz }));
        Assert.Empty(explorer.Match(new[] { 99 }));
    }

    [Fact]
    public void Artists_are_those_of_the_matching_tracks_with_the_most_tracks_first()
    {
        var artists = GenreExplorer.Build(Library()).Artists(new[] { Rock });

        Assert.Equal(new[] { "Anthrax", "Beatles" }, artists.Select(a => a.Name));
        Assert.Equal(new[] { 2, 2 }, artists.Select(a => a.Tracks));
        Assert.Equal(10, artists[0].AlbumId);
    }

    [Fact]
    public void An_artist_name_falls_back_to_the_album_artist_and_counts_once_per_spelling()
    {
        var genres = new[] { new CatalogGenre(Rock, "Rock") };
        var albums = new[] { new CatalogAlbum(10, "A", null, "Band", 1990, null, false, 1) };
        var tracks = new[] { T(1, 10, null), T(2, 10, "BAND"), T(3, 10, "") };
        var links = new Dictionary<int, int[]> { [Rock] = new[] { 1, 2, 3 } };
        var catalog = new Catalog(genres, Array.Empty<CatalogArtist>(), albums, tracks, null, genreTracks: links);

        var artists = GenreExplorer.Build(catalog).Artists(Array.Empty<int>());

        Assert.Single(artists);
        Assert.Equal(3, artists[0].Tracks);
    }

    [Fact]
    public void A_random_pick_has_distinct_matching_tracks_and_is_capped_by_what_matches()
    {
        var explorer = GenreExplorer.Build(Library());

        var three = explorer.Random(new[] { Rock }, 3, new Random(7));
        Assert.Equal(3, three.Count);
        Assert.Equal(3, three.Select(t => t.Id).Distinct().Count());
        Assert.All(three, t => Assert.Contains(t.Id, new[] { 1, 2, 3, 6 }));

        Assert.Equal(4, explorer.Random(new[] { Rock }, 100, new Random(7)).Count);
        Assert.Empty(explorer.Random(new[] { Metal, Jazz }, 10, new Random(7)));
        Assert.Empty(explorer.Random(new[] { Rock }, 0, new Random(7)));
    }

    [Fact]
    public void Without_stored_links_each_track_has_just_its_one_genre_name()
    {
        var explorer = GenreExplorer.Build(Library(withLinks: false));

        Assert.False(explorer.HasAllGenres);
        Assert.Equal(new[] { 3, 6 }, explorer.Match(new[] { Rock }).Select(t => t.Id).Order());
        Assert.Empty(explorer.Options(new[] { Rock })); // no track has two genres to go with Rock
    }

    [Fact]
    public void Links_to_tracks_the_index_does_not_have_are_ignored()
    {
        var genres = new[] { new CatalogGenre(Rock, "Rock") };
        var albums = new[] { new CatalogAlbum(10, "A", null, "Band", 1990, null, false, 1) };
        var catalog = new Catalog(genres, Array.Empty<CatalogArtist>(), albums, new[] { T(1, 10, "Band") }, null, genreTracks: new Dictionary<int, int[]> { [Rock] = new[] { 1, 777 } });

        Assert.Single(GenreExplorer.Build(catalog).Match(new[] { Rock }));
    }
}
