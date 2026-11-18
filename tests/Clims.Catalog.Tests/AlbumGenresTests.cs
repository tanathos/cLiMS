namespace Clims.Catalog.Tests;

public class AlbumGenresTests
{
    private static Catalog Build(bool withLinks)
    {
        var genres = new[] { new CatalogGenre(1, "Power Metal"), new CatalogGenre(2, "Heavy Metal") };
        var albums = new[] { new CatalogAlbum(10, "To the Metal!", 1, "Gamma Ray", 2010, null, false, 1) };
        var tracks = new[]
        {
            new CatalogTrack(1, 10, "Empathy", "Gamma Ray", 1, 1, 1, TimeSpan.FromMinutes(5), "Power Metal"),
            new CatalogTrack(2, 10, "Rise", "Gamma Ray", 1, 1, 2, TimeSpan.FromMinutes(5), "Power Metal"),
        };
        IReadOnlyDictionary<int, int[]>? links = withLinks
            ? new Dictionary<int, int[]> { [1] = [1, 2], [2] = [1] }
            : null;
        return new Catalog(genres, [], albums, tracks, null, genreTracks: links);
    }

    [Fact]
    public void AnAlbumShowsEveryGenreItsTracksCarryMostUsualFirst()
    {
        var catalog = Build(withLinks: true);

        Assert.Equal(["Power Metal", "Heavy Metal"], catalog.Summary(10).Genres);
        Assert.Single(catalog.AlbumsOfGenre("Heavy Metal"));
    }

    [Fact]
    public void WithoutTheStoredLinksTheOneGenreOfEachTrackIsUsed()
    {
        var catalog = Build(withLinks: false);

        Assert.Equal(["Power Metal"], catalog.Summary(10).Genres);
        Assert.Empty(catalog.AlbumsOfGenre("Heavy Metal"));
    }
}
