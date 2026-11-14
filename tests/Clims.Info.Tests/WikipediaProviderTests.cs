namespace Clims.Info.Tests;

public class WikipediaProviderTests
{
    private const string Page = """
        {"query":{"pages":[{"pageid":1,"title":"Helloween","extract":"Helloween is a German power metal band.\nThey formed in Hamburg in 1984."}]}}
        """;

    private static WikipediaProvider Provider(FakeHandler handler) => new(new HttpClient(handler));

    [Fact]
    public async Task TheWikidataEntryNamesTheArticleAndItsIntroductionIsTheBiography()
    {
        var handler = new FakeHandler()
            .On("wbgetentities", """{"entities":{"Q1":{"sitelinks":{"enwiki":{"title":"Helloween"}}}}}""")
            .On("prop=extracts", Page);

        var artist = await Provider(handler).GetArtistAsync(new InfoQuery("Helloween", "Keeper", WikidataId: "Q1"), CancellationToken.None);

        Assert.NotNull(artist);
        Assert.Equal("Helloween is a German power metal band.\n\nThey formed in Hamburg in 1984.", artist.Biography);
        Assert.Equal("wikipedia", artist.BiographySource);
        Assert.Equal(new InfoLink("wikipedia", "https://en.wikipedia.org/wiki/Helloween"), Assert.Single(artist.Links));
        Assert.DoesNotContain(handler.Requests, r => r.Contains("list=search"));
    }

    [Fact]
    public async Task AnEntityWithoutAnArticleHasNoBiographyAndIsNotSearched()
    {
        var handler = new FakeHandler().On("wbgetentities", """{"entities":{"Q1":{"sitelinks":{}}}}""");

        Assert.Null(await Provider(handler).GetArtistAsync(new InfoQuery("Band", "A", WikidataId: "Q1"), CancellationToken.None));
        Assert.DoesNotContain(handler.Requests, r => r.Contains("list=search"));
    }

    [Fact]
    public async Task WithoutAnEntityTheArtistIsSearchedAndMustBeAMusician()
    {
        var handler = new FakeHandler()
            .On("list=search", """{"query":{"search":[{"title":"Helloween (disambiguation)"},{"title":"Helloween"}]}}""")
            .On("prop=extracts", Page);

        var artist = await Provider(handler).GetArtistAsync(new InfoQuery("helloween", "Keeper"), CancellationToken.None);

        Assert.Equal("Helloween", artist!.Name);
    }

    [Fact]
    public async Task ASearchHitThatIsNotAboutMusicIsRejected()
    {
        var handler = new FakeHandler()
            .On("list=search", """{"query":{"search":[{"title":"Cream"}]}}""")
            .On("prop=extracts", """{"query":{"pages":[{"title":"Cream","extract":"Cream is the fatty part of milk."}]}}""");

        Assert.Null(await Provider(handler).GetArtistAsync(new InfoQuery("Cream", "Wheels"), CancellationToken.None));
    }

    [Fact]
    public async Task ADisambiguationPageIsNotABiography()
    {
        var handler = new FakeHandler()
            .On("wbgetentities", """{"entities":{"Q1":{"sitelinks":{"enwiki":{"title":"Band"}}}}}""")
            .On("prop=extracts", """{"query":{"pages":[{"title":"Band","extract":"Band may refer to:","pageprops":{"disambiguation":""}}]}}""");

        Assert.Null(await Provider(handler).GetArtistAsync(new InfoQuery("Band", "A", WikidataId: "Q1"), CancellationToken.None));
    }

    [Fact]
    public async Task VariousArtistsAreNotLookedUp()
    {
        var handler = new FakeHandler();

        Assert.Null(await Provider(handler).GetArtistAsync(new InfoQuery("Various Artists", "X"), CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    private const string AlbumPage = """
        {"query":{"pages":[{"title":"Keeper of the Seven Keys, Part I","extract":"Keeper of the Seven Keys, Part I is the second studio album by the German power metal band Helloween, released in 1987."}]}}
        """;

    [Fact]
    public async Task TheAlbumArticleComesFromItsWikidataEntry()
    {
        var handler = new FakeHandler()
            .On("wbgetentities", """{"entities":{"Q9":{"sitelinks":{"enwiki":{"title":"Keeper of the Seven Keys, Part I"}}}}}""")
            .On("prop=extracts", AlbumPage);

        var album = await Provider(handler).GetAlbumAsync(new InfoQuery("Helloween", "Keeper of the Seven Keys, Part I", AlbumWikidataId: "Q9"), CancellationToken.None);

        Assert.Contains("second studio album", album!.Description);
        Assert.Equal("wikipedia", album.DescriptionSource);
        Assert.Equal("wikipedia", Assert.Single(album.Links).Kind);
    }

    [Fact]
    public async Task AnAlbumFoundBySearchMustBeARecordByTheArtist()
    {
        var search = """{"query":{"search":[{"title":"Keeper of the Seven Keys, Part I"}]}}""";
        var good = new FakeHandler().On("list=search", search).On("prop=extracts", AlbumPage);
        var other = new FakeHandler().On("list=search", search).On("prop=extracts", """{"query":{"pages":[{"title":"Keeper of the Seven Keys, Part I","extract":"A novel about a keeper, published in 1950."}]}}""");

        Assert.NotNull(await Provider(good).GetAlbumAsync(new InfoQuery("Helloween", "Keeper of the Seven Keys, Part I"), CancellationToken.None));
        Assert.Null(await Provider(other).GetAlbumAsync(new InfoQuery("Helloween", "Keeper of the Seven Keys, Part I"), CancellationToken.None));
        Assert.Null(await Provider(good).GetAlbumAsync(new InfoQuery("Gamma Ray", "Keeper of the Seven Keys, Part I"), CancellationToken.None));
    }
}
