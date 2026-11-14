namespace Clims.Info.Tests;

public class MusicBrainzProviderTests
{
    private const string ArtistSearch = """
        {"artists":[
          {"id":"wrong","name":"Helloween Tribute","score":100},
          {"id":"a1","name":"Helloween","score":100}]}
        """;

    private const string ArtistPage = """
        {"id":"a1","name":"Helloween","type":"Group","country":"DE","area":{"name":"Germany"},
         "life-span":{"begin":"1984","end":null,"ended":false},"disambiguation":"German power metal band",
         "annotation":"Founded in [[Hamburg]] by '''Kai Hansen'''.",
         "genres":[{"name":"power metal","count":9},{"name":"heavy metal","count":3}],
         "tags":[{"name":"ignored","count":50}],
         "relations":[
           {"type":"member of band","target-type":"artist","direction":"backward","ended":false,"begin":"1984","attributes":["guitar","vocals"],"artist":{"name":"Michael Weikath"}},
           {"type":"member of band","target-type":"artist","direction":"backward","ended":true,"begin":"1984","end":"1989","attributes":[],"artist":{"name":"Kai Hansen"}},
           {"type":"member of band","target-type":"artist","direction":"forward","artist":{"name":"Not a member"}},
           {"type":"official homepage","target-type":"url","url":{"resource":"https://www.helloween.org/"}},
           {"type":"image","target-type":"url","url":{"resource":"https://commons.wikimedia.org/wiki/File:Helloween 2018.jpg"}}]}
        """;

    private static MusicBrainzProvider Provider(FakeHandler handler) => new(new HttpClient(handler), TimeSpan.Zero);

    [Fact]
    public async Task TheArtistIsFoundByExactNameAndReadFromItsPage()
    {
        var handler = new FakeHandler().On("artist?query=", ArtistSearch).On("artist/a1?", ArtistPage);

        var artist = await Provider(handler).GetArtistAsync(new InfoQuery("helloween", "Keeper"), CancellationToken.None);

        Assert.NotNull(artist);
        Assert.Equal(("a1", "Helloween", "Group", "DE", "Germany", "1984"), (artist.Id, artist.Name, artist.Kind, artist.Country, artist.Area, artist.Begin));
        Assert.False(artist.Ended);
        Assert.Equal(["power metal", "heavy metal"], artist.Genres);
        Assert.Equal("Founded in Hamburg by Kai Hansen.", artist.Biography);
    }

    [Fact]
    public async Task MembersAreTheBackwardBandRelationsCurrentOnesFirst()
    {
        var handler = new FakeHandler().On("artist?query=", ArtistSearch).On("artist/a1?", ArtistPage);

        var artist = await Provider(handler).GetArtistAsync(new InfoQuery("Helloween", "Keeper"), CancellationToken.None);

        Assert.Equal(["Michael Weikath", "Kai Hansen"], artist!.Members.Select(m => m.Name));
        Assert.Equal("guitar, vocals", artist.Members[0].Role);
        Assert.True(artist.Members[0].Current);
        Assert.Equal("1989", artist.Members[1].End);
    }

    [Fact]
    public async Task LinksAreKeptAndACommonsPageBecomesAPictureAddress()
    {
        var handler = new FakeHandler().On("artist?query=", ArtistSearch).On("artist/a1?", ArtistPage);

        var artist = await Provider(handler).GetArtistAsync(new InfoQuery("Helloween", "Keeper"), CancellationToken.None);

        Assert.Equal(new InfoLink("official homepage", "https://www.helloween.org/"), Assert.Single(artist!.Links));
        Assert.Equal("https://commons.wikimedia.org/wiki/Special:FilePath/Helloween%202018.jpg?width=900", Assert.Single(artist.ImageUrls));
    }

    [Fact]
    public async Task WithoutAPictureLinkTheWikidataEntryProvidesOne()
    {
        var page = """
            {"id":"a1","name":"Band","relations":[
              {"type":"wikidata","target-type":"url","url":{"resource":"https://www.wikidata.org/wiki/Q42"}}]}
            """;
        var handler = new FakeHandler()
            .On("artist?query=", """{"artists":[{"id":"a1","name":"Band","score":100}]}""")
            .On("artist/a1?", page)
            .On("Special:EntityData/Q42.json", """{"entities":{"Q42":{"claims":{"P18":[{"mainsnak":{"datavalue":{"value":"The Band.jpg"}}}]}}}}""");

        var artist = await Provider(handler).GetArtistAsync(new InfoQuery("Band", "Album"), CancellationToken.None);

        Assert.Equal("https://commons.wikimedia.org/wiki/Special:FilePath/The%20Band.jpg?width=900", Assert.Single(artist!.ImageUrls));
    }

    [Fact]
    public async Task AWikidataEntryWithoutAPictureLeavesTheArtistWithout()
    {
        var handler = new FakeHandler()
            .On("artist?query=", """{"artists":[{"id":"a1","name":"Band","score":100}]}""")
            .On("artist/a1?", """{"id":"a1","name":"Band","relations":[{"type":"wikidata","target-type":"url","url":{"resource":"https://www.wikidata.org/wiki/Q42"}}]}""")
            .On("Special:EntityData/Q42.json", """{"entities":{"Q42":{"claims":{}}}}""");

        Assert.Empty((await Provider(handler).GetArtistAsync(new InfoQuery("Band", "Album"), CancellationToken.None))!.ImageUrls);
    }

    [Fact]
    public async Task LinksAreRankedAndOnePerKind()
    {
        var page = """
            {"id":"a1","name":"Band","relations":[
              {"type":"other databases","target-type":"url","url":{"resource":"https://a.example/1"}},
              {"type":"other databases","target-type":"url","url":{"resource":"https://a.example/2"}},
              {"type":"bandcamp","target-type":"url","url":{"resource":"https://band.bandcamp.com"}},
              {"type":"official homepage","target-type":"url","url":{"resource":"https://band.example"}}]}
            """;
        var handler = new FakeHandler().On("artist?query=", """{"artists":[{"id":"a1","name":"Band","score":100}]}""").On("artist/a1?", page);

        var artist = await Provider(handler).GetArtistAsync(new InfoQuery("Band", "Album"), CancellationToken.None);

        Assert.Equal(["official homepage", "bandcamp", "other databases"], artist!.Links.Select(l => l.Kind));
    }

    [Fact]
    public async Task AnArtistWhoseNameDiffersIsNotAMatch()
    {
        var handler = new FakeHandler().On("artist?query=", """{"artists":[{"id":"x","name":"Helloween Tribute","score":100}]}""");
        Assert.Null(await Provider(handler).GetArtistAsync(new InfoQuery("Helloween", "Keeper"), CancellationToken.None));
    }

    [Fact]
    public async Task VariousArtistsIsNotLookedUp()
    {
        var handler = new FakeHandler();
        Assert.Null(await Provider(handler).GetArtistAsync(new InfoQuery("Various Artists", "Compilation"), CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TheAlbumComesFromItsReleaseGroupAndTheEarliestOfficialRelease()
    {
        var handler = new FakeHandler()
            .On("release-group?query=", """{"release-groups":[{"id":"g1","title":"Keeper of the Seven Keys, Part I","score":100}]}""")
            .On("release-group/g1?", """
                {"id":"g1","title":"Keeper of the Seven Keys, Part I","primary-type":"Album","secondary-types":["Remix"],"first-release-date":"1987-05-23",
                 "genres":[{"name":"power metal","count":5}],"rating":{"value":4.5,"votes-count":12},
                 "releases":[{"id":"r2","date":"1990","status":"Official"},{"id":"r1","date":"1987-05-23","status":"Official"},{"id":"r0","date":"1986","status":"Bootleg"}]}
                """)
            .On("release/r1?", """
                {"country":"DE","label-info":[{"catalog-number":"RR 9748","label":{"name":"Noise"}}],"media":[{"track-count":5},{"track-count":3}]}
                """);

        var album = await Provider(handler).GetAlbumAsync(new InfoQuery("Helloween", "Keeper of the Seven Keys, Part I", ArtistId: "a1"), CancellationToken.None);

        Assert.NotNull(album);
        Assert.Equal(("Album", "1987-05-23", "Noise", "RR 9748", "DE", 8), (album.Kind, album.FirstReleased, album.Label, album.CatalogNumber, album.Country, album.TrackCount));
        Assert.Equal(["Remix"], album.Secondary);
        Assert.Equal((4.5, 12), (album.Rating, album.Votes));
        Assert.Contains(handler.Requests, r => r.Contains("arid:a1"));
    }

    [Fact]
    public async Task AFailingReleaseDoesNotHideTheAlbum()
    {
        var handler = new FakeHandler()
            .On("release-group?query=", """{"release-groups":[{"id":"g1","title":"Album","score":100}]}""")
            .On("release-group/g1?", """{"id":"g1","title":"Album","primary-type":"Album","releases":[{"id":"r1","date":"2000","status":"Official"}]}""");

        var album = await Provider(handler).GetAlbumAsync(new InfoQuery("Artist", "Album"), CancellationToken.None);

        Assert.Equal("Album", album!.Title);
        Assert.Null(album.Label);
    }

    [Theory]
    [InlineData("https://commons.wikimedia.org/wiki/File:Band.jpg", "https://commons.wikimedia.org/wiki/Special:FilePath/Band.jpg?width=900")]
    [InlineData("https://example.org/pic.PNG", "https://example.org/pic.PNG")]
    [InlineData("https://example.org/page.html", null)]
    [InlineData("not a url", null)]
    public void ImageAddressesAreRecognised(string resource, string? expected) =>
        Assert.Equal(expected, MusicBrainzProvider.ImageAddress(resource));

    [Fact]
    public void AnnotationsLoseTheirWikiMarkup() =>
        Assert.Equal("See Hamburg and the site.", MusicBrainzProvider.PlainText("See [[Hamburg|Hamburg]] and [https://example.org the site]."));
}
