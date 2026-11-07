namespace Clims.Lms.Tests;

public class LibraryApiTests
{
    [Fact]
    public async Task Albums_are_mapped_from_the_recorded_reply()
    {
        var (client, handler) = TestClient.FromFixture("albums");
        var page = await client.Library.GetAlbumsAsync(count: 5);

        Assert.Equal(5, page.Items.Count);
        Assert.Equal(2008, page.Total);
        Assert.True(page.HasMore);

        var first = page.Items[0];
        Assert.Equal(853519, first.Id);
        Assert.Equal("+-", first.Title);
        Assert.Equal("Mew", first.ArtistName);
        Assert.Equal(437329, first.ArtistId);
        Assert.Equal(2015, first.Year);
        Assert.Equal("2836a7e5", first.ArtworkTrackId);
        Assert.Equal("+", first.TextKey);
        Assert.False(first.IsCompilation);
        Assert.Equal("ALBUM", first.ReleaseType);

        Assert.Equal(2, page.Items[1].DiscCount);
        Assert.Equal(0, page.Items[2].Year);
        Assert.Null(page.Items[4].ArtworkTrackId); // album without artwork
        Assert.Equal("10 Años Un Panteón Muy Vivo", page.Items[4].Title); // accents survive

        Assert.Equal("albums 0 5000".Split(' ').Length + 2, handler.Requests[0].Command.Length);
    }

    [Fact]
    public async Task Album_query_becomes_filters_tags_and_sort()
    {
        var (client, handler) = TestClient.FromFixture("albums_empty");
        await client.Library.GetAlbumsAsync(
            new AlbumQuery { ArtistId = 12, GenreId = 3, Year = 2015, Compilation = false, Search = "iron", Sort = AlbumSort.YearAlbum, Tags = AlbumTags.Album | AlbumTags.ArtworkTrackId },
            start: 100,
            count: 50);

        var c = handler.Requests[0].Command;
        Assert.Equal(new[] { "albums", "100", "50", "tags:lj", "sort:yearalbum" }, c.Take(5));
        Assert.Contains("artist_id:12", c);
        Assert.Contains("genre_id:3", c);
        Assert.Contains("year:2015", c);
        Assert.Contains("compilation:0", c);
        Assert.Contains("search:iron", c);
        Assert.Equal(string.Empty, handler.Requests[0].PlayerId);
    }

    [Fact]
    public async Task No_matches_is_an_empty_page_not_an_error()
    {
        var (client, _) = TestClient.FromFixture("albums_empty");
        var page = await client.Library.GetAlbumsAsync();
        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Tracks_cope_with_numbers_typed_as_strings()
    {
        var (client, _) = TestClient.FromFixture("titles");
        var page = await client.Library.GetTracksAsync(new TrackQuery { AlbumId = 853519 }, count: 6);

        Assert.Equal(6, page.Items.Count);
        Assert.Equal(10, page.Total);
        var t = page.Items[0];
        Assert.Equal(8547903, t.Id);
        Assert.Equal("Satellites", t.Title);
        Assert.Equal("Mew", t.Artist);
        Assert.Equal(1, t.TrackNumber);            // "tracknum":"1"
        Assert.Equal(2015, t.Year);                // "year":"2015"
        Assert.Equal(853519, t.AlbumId);           // "album_id":"853519"
        Assert.Equal(437329, t.ArtistId);
        Assert.Equal(TimeSpan.FromSeconds(369.373), t.Duration);
        Assert.Equal(false, t.IsLossless);         // "lossless":"0"
        Assert.Equal(false, t.IsCompilation);
        Assert.Equal(44100, t.SampleRate);
        Assert.Equal("320kbps CBR", t.Bitrate);
        Assert.Equal("Alternative Rock", t.Genre);
        Assert.Equal(85292, t.GenreId);
        Assert.Equal("2836a7e5", t.ArtworkTrackId);
        Assert.Equal("852b145a", t.CoverId);
        Assert.EndsWith("01%20-%20Satellites.mp3", t.Url);
        Assert.Null(t.DiscNumber);                 // not requested/returned
    }

    [Fact]
    public async Task Track_query_asks_for_the_album_and_the_requested_tags()
    {
        var (client, handler) = TestClient.FromFixture("titles");
        await client.Library.GetTracksAsync(new TrackQuery { AlbumId = 5, Tags = TrackTags.Artist | TrackTags.Url, Sort = TrackSort.TrackNumber }, 10, 20);
        Assert.Equal(new[] { "titles", "10", "20", "tags:au", "sort:tracknum", "album_id:5" }, handler.Requests[0].Command);
    }

    [Fact]
    public async Task Empty_titles_reply_is_an_empty_page()
    {
        var (client, _) = TestClient.FromFixture("titles_empty");
        var page = await client.Library.GetTracksAsync(new TrackQuery { Search = "zzz" });
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Artists_and_genres_are_mapped()
    {
        var (artists, _) = TestClient.FromFixture("artists");
        var a = await artists.Library.GetArtistsAsync();
        Assert.Equal(1004, a.Total);
        Assert.Equal("The 13th Floor Elevators", a.Items[0].Name);
        Assert.Equal(437617, a.Items[0].Id);

        var (genres, _) = TestClient.FromFixture("genres");
        var g = await genres.Library.GetGenresAsync();
        Assert.Equal(195, g.Total);
        Assert.Equal("Abstract", g.Items[0].Name);
        Assert.Equal(85397, g.Items[0].Id);
    }

    [Fact]
    public async Task Search_reads_the_four_groups_and_their_totals()
    {
        var (client, handler) = TestClient.FromFixture("search");
        var r = await client.Library.SearchAsync("iron", 5);

        Assert.Equal(new[] { "search", "0", "5", "term:iron" }, handler.Requests[0].Command);
        Assert.Equal(2, r.ArtistCount);
        Assert.Equal("Iron Maiden", r.Artists[0].Name);
        Assert.Equal(437214, r.Artists[0].Id);
        Assert.Equal(4, r.AlbumCount);
        Assert.Equal(4, r.Albums.Count);
        Assert.Equal(29, r.TrackCount);
        Assert.Equal(5, r.Tracks.Count);
        Assert.Equal(8537238, r.Tracks[0].Id);
        Assert.Empty(r.Genres);
        Assert.Equal(0, r.GenreCount);
    }

    [Fact]
    public async Task Search_without_hits_is_empty()
    {
        var (client, _) = TestClient.FromFixture("search_empty");
        var r = await client.Library.SearchAsync("zzzqqqxxx");
        Assert.Empty(r.Artists);
        Assert.Empty(r.Albums);
        Assert.Empty(r.Tracks);
        Assert.Equal(0, r.TrackCount);
    }

    [Fact]
    public async Task Songinfo_fields_are_merged_into_one_track()
    {
        var (client, handler) = TestClient.FromFixture("songinfo");
        var t = await client.Library.GetTrackByUrlAsync("file:///mnt/Music/Mew/+%20-/01%20-%20Satellites.mp3");

        Assert.NotNull(t);
        Assert.Equal(8547903, t!.Id);
        Assert.Equal("Satellites", t.Title);
        Assert.Equal(2015, t.Year);
        Assert.Equal(false, t.IsLossless);
        Assert.Contains(handler.Requests[0].Command, w => w.StartsWith("url:file:///mnt/Music/Mew/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_track_by_url_is_null()
    {
        var handler = new FakeHandler(_ => FakeHandler.Result("{}"));
        var client = TestClient.Create(handler);
        Assert.Null(await client.Library.GetTrackByUrlAsync("file:///nope.flac"));
    }

    [Fact]
    public async Task Track_by_id_uses_a_one_item_titles_query_and_returns_null_when_missing()
    {
        var (client, handler) = TestClient.FromFixture("titles_empty");
        Assert.Null(await client.Library.GetTrackAsync(42));
        var c = handler.Requests[0].Command;
        Assert.Equal(new[] { "titles", "0", "1" }, c.Take(3));
        Assert.Contains("track_id:42", c);
    }

    [Fact]
    public async Task No_saved_playlists_gives_an_empty_page()
    {
        // LMS answers {} (not even a count) when none exist.
        var (client, _) = TestClient.FromFixture("playlists");
        var page = await client.Library.GetPlaylistsAsync();
        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task Library_reads_are_flagged_retryable()
    {
        var attempts = 0;
        var handler = new FakeHandler(_ =>
        {
            if (++attempts == 1)
            {
                throw new HttpRequestException(HttpRequestError.ConnectionError, "blip");
            }

            return FakeHandler.Json(Fixtures.Load("genres"));
        });
        var client = TestClient.Create(handler);
        var page = await client.Library.GetGenresAsync();
        Assert.Equal(2, attempts);
        Assert.Equal(195, page.Total);
    }
}
