using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms.Tests;

public class JsonReadingTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Numbers_are_read_from_numbers_and_numeric_strings()
    {
        var e = Parse("""{"a":5,"b":"7","c":"x","d":2015.0,"e":null}""");
        Assert.Equal(5, e.GetInt("a"));
        Assert.Equal(7, e.GetInt("b"));
        Assert.Equal(-1, e.GetInt("c", -1));
        Assert.Equal(2015, e.GetInt("d"));
        Assert.Null(e.GetIntOrNull("e"));
        Assert.Null(e.GetIntOrNull("missing"));
    }

    [Fact]
    public void Doubles_survive_as_number_or_string_and_ignore_locale()
    {
        var e = Parse("""{"time":78.9679701118469,"duration":"421.906"}""");
        Assert.Equal(78.9679701118469, e.GetDouble("time"));
        Assert.Equal(421.906, e.GetDouble("duration"));
        Assert.Equal(TimeSpan.FromSeconds(421.906), e.GetSeconds("duration"));
    }

    [Theory]
    [InlineData("""{"v":1}""", true)]
    [InlineData("""{"v":"1"}""", true)]
    [InlineData("""{"v":true}""", true)]
    [InlineData("""{"v":0}""", false)]
    [InlineData("""{"v":"0"}""", false)]
    [InlineData("""{"v":false}""", false)]
    [InlineData("""{"v":null}""", false)]
    [InlineData("""{}""", false)]
    public void Booleans_accept_the_many_ways_LMS_writes_them(string json, bool expected) =>
        Assert.Equal(expected, Parse(json).GetBool("v"));

    [Fact]
    public void Strings_come_back_for_numbers_too()
    {
        var e = Parse("""{"id":853519,"year":"2015","none":null}""");
        Assert.Equal("853519", e.GetStringOrNull("id"));
        Assert.Equal("2015", e.GetStringOrNull("year"));
        Assert.Null(e.GetStringOrNull("none"));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"count":0}""")]
    [InlineData("""{"x_loop":null}""")]
    public void A_missing_loop_is_an_empty_sequence(string json) =>
        Assert.Empty(Parse(json).GetLoop("x_loop"));

    [Fact]
    public void A_present_loop_is_enumerated() =>
        Assert.Equal(2, Parse("""{"x_loop":[{"id":1},{"id":2}]}""").GetLoop("x_loop").Count());
}

public class TagTests
{
    [Fact]
    public void No_flags_gives_an_empty_tag_list() => Assert.Equal("tags:", TrackTags.None.ToTagArgument());

    [Fact]
    public void Track_flags_map_to_their_letters() =>
        Assert.Equal("tags:aldu", (TrackTags.Artist | TrackTags.Album | TrackTags.Duration | TrackTags.Url).ToTagArgument());

    [Fact]
    public void Letters_do_not_depend_on_the_order_flags_were_combined()
    {
        var a = (TrackTags.Url | TrackTags.Artist).ToTagArgument();
        var b = (TrackTags.Artist | TrackTags.Url).ToTagArgument();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Every_track_flag_has_a_distinct_letter()
    {
        var letters = TrackTags.All.ToTagArgument()["tags:".Length..];
        Assert.Equal(letters.Length, letters.Distinct().Count());
        Assert.Equal(19, letters.Length);
    }

    [Fact]
    public void Album_tags_use_the_album_letters_not_the_track_ones()
    {
        // Artwork is 'j' on albums but 'J' on tracks; artist id is 'S' on albums but 's' on tracks.
        Assert.Equal("tags:jS", (AlbumTags.ArtworkTrackId | AlbumTags.ArtistId).ToTagArgument());
        Assert.Equal("tags:Js", (TrackTags.ArtworkTrackId | TrackTags.ArtistId).ToTagArgument());
    }

    [Fact]
    public void Default_album_list_asks_for_artwork_and_text_key() =>
        Assert.All(new[] { 'j', 's', 'y', 'a' }, c => Assert.Contains(c, AlbumTags.List.ToTagArgument()));
}

public class ArtworkAndAudioUrlTests
{
    private static readonly Uri Base = new("http://192.168.1.20:9000/");

    [Fact]
    public void Cover_url_for_an_artwork_id() =>
        Assert.Equal("http://192.168.1.20:9000/music/2836a7e5/cover_96x96_o.jpg", ArtworkUrl.Cover(Base, "2836a7e5", 96, 96).ToString());

    [Fact]
    public void Cover_url_for_a_track_id_and_png() =>
        Assert.Equal("http://192.168.1.20:9000/music/8547903/cover_300x300_o.png", ArtworkUrl.Cover(Base, 8547903, 300, 300, ArtworkFormat.Png).ToString());

    [Fact]
    public void Cover_url_respects_a_base_path()
    {
        var url = ArtworkUrl.Cover(new Uri("http://host/lms/"), "ab", 48, 48);
        Assert.Equal("http://host/lms/music/ab/cover_48x48_o.jpg", url.ToString());
    }

    [Fact]
    public void Cover_url_rejects_blank_ids() =>
        Assert.Throws<ArgumentException>(() => ArtworkUrl.Cover(Base, " ", 48, 48));

    [Fact]
    public void Download_url() =>
        Assert.Equal("http://192.168.1.20:9000/music/8547903/download", AudioUrl.Download(Base, 8547903).ToString());
}

public class PagingTests
{
    private static Func<int, int, CancellationToken, Task<Page<int>>> Source(int total, List<(int Start, int Count)> calls) =>
        (start, count, _) =>
        {
            calls.Add((start, count));
            var items = Enumerable.Range(start, Math.Max(0, Math.Min(count, total - start))).ToList();
            return Task.FromResult(new Page<int>(items, total, start));
        };

    [Fact]
    public async Task Walks_every_page_in_order()
    {
        var calls = new List<(int, int)>();
        var all = new List<int>();
        await foreach (var i in Paging.EnumerateAsync(Source(25, calls), 10))
        {
            all.Add(i);
        }

        Assert.Equal(Enumerable.Range(0, 25), all);
        Assert.Equal(new[] { (0, 10), (10, 10), (20, 10) }, calls);
    }

    [Fact]
    public async Task An_exact_multiple_does_not_ask_for_an_extra_page()
    {
        var calls = new List<(int, int)>();
        await foreach (var _ in Paging.EnumerateAsync(Source(20, calls), 10))
        {
        }

        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public async Task Stops_on_an_empty_page_even_if_the_total_is_larger()
    {
        // A library that shrinks while paging must not loop forever.
        var n = 0;
        Task<Page<int>> Fetch(int start, int count, CancellationToken ct) =>
            Task.FromResult(n++ == 0 ? new Page<int>(new[] { 1, 2 }, 100, start) : new Page<int>(Array.Empty<int>(), 100, start));

        var all = new List<int>();
        await foreach (var i in Paging.EnumerateAsync(Fetch, 2))
        {
            all.Add(i);
        }

        Assert.Equal(new[] { 1, 2 }, all);
    }

    [Fact]
    public async Task Empty_result_yields_nothing()
    {
        var calls = new List<(int, int)>();
        await foreach (var _ in Paging.EnumerateAsync(Source(0, calls), 10))
        {
            Assert.Fail("no items expected");
        }

        Assert.Single(calls);
    }

    [Fact]
    public async Task Cancellation_stops_the_enumeration()
    {
        using var cts = new CancellationTokenSource();
        var calls = new List<(int, int)>();
        var seen = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in Paging.EnumerateAsync(
                               async (s, c, t) =>
                               {
                                   t.ThrowIfCancellationRequested();
                                   return await Source(1000, calls)(s, c, t);
                               },
                               10,
                               cts.Token))
            {
                if (++seen == 15)
                {
                    cts.Cancel();
                }
            }
        });
        Assert.True(seen < 1000);
    }

    [Fact]
    public void Page_knows_whether_more_follow()
    {
        Assert.True(new Page<int>(new[] { 1, 2 }, 5, 0).HasMore);
        Assert.False(new Page<int>(new[] { 4, 5 }, 5, 3).HasMore);
    }
}
