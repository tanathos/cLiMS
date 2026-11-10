using System.Net;

namespace Clims.Lyrics.Tests;

public class ProviderTests
{
    private static readonly LyricsQuery Song = new("Slayer", "Reign In Blood", "Raining Blood", TimeSpan.FromSeconds(257), "Slayer");

    private static HttpClient Client(StubHandler handler) => new(handler);

    [Fact]
    public async Task Lrclib_exact_lookup_returns_synced_and_plain_lyrics()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, """{"plainLyrics":"a\nb","syncedLyrics":"[00:01.00]a\n[00:02.00]b","instrumental":false}"""));

        var result = await new LrclibProvider(Client(handler)).FindAsync(Song, default);

        Assert.True(result!.IsSynced);
        Assert.Equal("a\nb", result.Plain);
        var url = handler.Requests.Single().AbsoluteUri;
        Assert.Contains("artist_name=Slayer", url);
        Assert.Contains("track_name=Raining%20Blood", url);
        Assert.Contains("duration=257", url);
        Assert.StartsWith("cLiMS", handler.UserAgents.Single());
    }

    [Fact]
    public async Task Lrclib_without_plain_text_derives_it_from_the_synced_lines()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, """{"plainLyrics":null,"syncedLyrics":"[00:01.00]one\n[00:02.00]two"}"""));

        var result = await new LrclibProvider(Client(handler)).FindAsync(Song, default);

        Assert.Equal("one\ntwo", result!.Plain);
    }

    [Fact]
    public async Task Lrclib_reports_an_instrumental()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, """{"instrumental":true,"plainLyrics":null,"syncedLyrics":null}"""));

        var result = await new LrclibProvider(Client(handler)).FindAsync(Song, default);

        Assert.True(result!.Instrumental);
    }

    [Fact]
    public async Task Lrclib_falls_back_to_a_search_and_skips_other_versions()
    {
        var handler = new StubHandler(uri => uri.AbsolutePath.EndsWith("/get", StringComparison.Ordinal)
            ? (HttpStatusCode.NotFound, "{}")
            : (HttpStatusCode.OK, """
                [
                  {"trackName":"Raining Blood","artistName":"Slayer","duration":152.9,"plainLyrics":"live version"},
                  {"trackName":"Raining Blood","artistName":"Someone Else","duration":257,"plainLyrics":"cover"},
                  {"trackName":"Raining Blood","artistName":"Slayer","duration":258,"plainLyrics":"studio"}
                ]
                """));

        var result = await new LrclibProvider(Client(handler)).FindAsync(Song, default);

        Assert.Equal("studio", result!.Plain);
    }

    [Fact]
    public async Task Lrclib_finding_nothing_is_null_and_a_server_error_is_an_exception()
    {
        var nothing = new StubHandler(_ => (HttpStatusCode.NotFound, "{}"));
        Assert.Null(await new LrclibProvider(Client(nothing)).FindAsync(Song, default));

        var broken = new StubHandler(_ => (HttpStatusCode.InternalServerError, ""));
        await Assert.ThrowsAsync<HttpRequestException>(() => new LrclibProvider(Client(broken)).FindAsync(Song, default));
    }

    [Fact]
    public async Task LyricsOvh_returns_plain_text_without_the_site_heading()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, """{"lyrics":"Paroles de la chanson Raining Blood par Slayer\r\nTrapped in purgatory\r\nA lifeless object"}"""));

        var result = await new LyricsOvhProvider(Client(handler)).FindAsync(Song, default);

        Assert.Equal("Trapped in purgatory\nA lifeless object", result!.Plain);
        Assert.False(result.IsSynced);
        Assert.Contains("/v1/Slayer/Raining%20Blood", handler.Requests.Single().AbsolutePath);
    }

    [Fact]
    public async Task LyricsOvh_not_found_is_null()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.NotFound, """{"error":"No lyrics found"}"""));

        Assert.Null(await new LyricsOvhProvider(Client(handler)).FindAsync(Song, default));
    }

    private const string AlbumPage = """
        <html><body><div class="lyrics">
        <h3><a name="1">1. Angel Of Death</a></h3><br />
        <i>[Lyrics &amp; Music - Someone]</i><br />
        <br />
        First line<br />
        Second &amp; third<br />
        <br />
        <h3><a name="2">2. Raining Blood</a></h3><br />
        Trapped in purgatory<br />
        A lifeless object<br />
        <br /><div class="thanks">Thanks to somebody</div><br />
        </div></body></html>
        """;

    [Fact]
    public void The_album_page_parser_splits_songs_and_drops_credits_and_tags()
    {
        var songs = DarkLyricsParser.Parse(AlbumPage);

        Assert.Equal(2, songs.Count);
        Assert.Equal((1, "Angel Of Death", "First line\nSecond & third"), songs[0]);
        Assert.Equal((2, "Raining Blood", "Trapped in purgatory\nA lifeless object"), songs[1]);
    }

    [Fact]
    public void A_page_that_is_not_an_album_page_has_no_songs()
    {
        Assert.Empty(DarkLyricsParser.Parse("<html><title>Page not Found</title></html>"));
    }

    [Fact]
    public void Album_addresses_use_lower_case_names_without_spaces_or_symbols()
    {
        Assert.Equal(["http://www.darklyrics.com/lyrics/slayer/reigninblood.html"], DarkLyricsUrl.For("Slayer", "Reign In Blood"));
        Assert.Equal(
            ["http://www.darklyrics.com/lyrics/thehaunted/rulesofengagement.html", "http://www.darklyrics.com/lyrics/haunted/rulesofengagement.html"],
            DarkLyricsUrl.For("The Haunted", "Rules of Engagement (Deluxe Edition)"));
    }

    [Fact]
    public async Task DarkLyrics_returns_the_song_and_the_rest_of_the_album_as_siblings()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, AlbumPage));

        var result = await new DarkLyricsProvider(Client(handler), TimeSpan.Zero).FindAsync(Song, default);

        Assert.Equal("Trapped in purgatory\nA lifeless object", result!.Plain);
        var sibling = Assert.Single(result.Siblings);
        Assert.Equal("Angel Of Death", sibling.Query.Title);
        Assert.Equal("First line\nSecond & third", sibling.Result.Plain);
        Assert.Equal("http://www.darklyrics.com/lyrics/slayer/reigninblood.html", handler.Requests.Single().ToString());
    }

    [Fact]
    public async Task DarkLyrics_tries_the_next_address_and_gives_up_when_the_song_is_not_on_the_page()
    {
        var calls = 0;
        var handler = new StubHandler(uri => ++calls == 1 ? (HttpStatusCode.NotFound, "") : (HttpStatusCode.OK, AlbumPage));
        var provider = new DarkLyricsProvider(Client(handler), TimeSpan.Zero);

        var the = new LyricsQuery("The Slayer", "Reign In Blood", "Raining Blood", TimeSpan.Zero);
        Assert.Equal("Trapped in purgatory\nA lifeless object", (await provider.FindAsync(the, default))!.Plain);

        Assert.Null(await provider.FindAsync(Song with { Title = "No Such Song" }, default));
    }

    [Fact]
    public async Task DarkLyrics_skips_compilations_without_a_request()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, AlbumPage));

        Assert.Null(await new DarkLyricsProvider(Client(handler), TimeSpan.Zero).FindAsync(Song with { AlbumArtist = "Various Artists" }, default));
        Assert.Empty(handler.Requests);
    }
}
