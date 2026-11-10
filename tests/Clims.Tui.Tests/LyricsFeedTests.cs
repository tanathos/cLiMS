using Clims.Catalog;
using Clims.Lyrics;
using Clims.Tui.Data;

namespace Clims.Tui.Tests;

public class LyricsFeedTests
{
    private static readonly CatalogAlbum Album = new(1, "Reign In Blood", 7, "Slayer", 1986, null, false, 1);
    private static readonly NowPlaying Playing = new(Album, new CatalogTrack(10, 1, "Raining Blood", null, null, 1, 10, TimeSpan.FromSeconds(257), "Thrash Metal"));

    private sealed class Provider : ILyricsProvider
    {
        private readonly Func<LyricsQuery, LyricsResult?> _answer;

        public Provider(Func<LyricsQuery, LyricsResult?> answer) => _answer = answer;

        public string Name => "test";

        public int Calls { get; private set; }

        public Task<LyricsResult?> FindAsync(LyricsQuery query, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_answer(query));
        }
    }

    private static string Db() => Path.Combine(Path.GetTempPath(), "clims-feed-" + Guid.NewGuid().ToString("N") + ".db");

    private static Task Settled(LyricsFeed feed)
    {
        var done = new TaskCompletionSource();
        feed.Changed += () =>
        {
            if (feed.State is LyricsState.Found or LyricsState.NotFound)
            {
                done.TrySetResult();
            }
        };
        return done.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void The_query_uses_the_album_artist_when_the_track_has_none()
    {
        var query = LyricsFeed.QueryOf(Playing);

        Assert.Equal("Slayer", query.Artist);
        Assert.Equal("Reign In Blood", query.Album);
        Assert.Equal("Raining Blood", query.Title);
        Assert.Equal(TimeSpan.FromSeconds(257), query.Duration);
    }

    [Fact]
    public async Task A_search_finds_stores_and_reports_the_lyrics()
    {
        var provider = new Provider(_ => new LyricsResult("words", null, "test"));
        var service = new LyricsService(new LyricsStore(Db()), [provider], TimeSpan.FromDays(1));
        using var feed = new LyricsFeed(service);
        var settled = Settled(feed);

        feed.Show(Playing);
        await settled;

        Assert.Equal(LyricsState.Found, feed.State);
        Assert.Equal("words", feed.Result!.Plain);
    }

    [Fact]
    public async Task Showing_the_same_track_again_does_not_search_again()
    {
        var provider = new Provider(_ => new LyricsResult("words", null, "test"));
        var service = new LyricsService(new LyricsStore(Db()), [provider], TimeSpan.FromDays(1));
        using var feed = new LyricsFeed(service);
        var settled = Settled(feed);
        feed.Show(Playing);
        await settled;

        feed.Show(Playing);
        feed.Show(Playing);

        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Nothing_found_is_reported_as_such()
    {
        var service = new LyricsService(new LyricsStore(Db()), [new Provider(_ => null)], TimeSpan.FromDays(1));
        using var feed = new LyricsFeed(service);
        var settled = Settled(feed);

        feed.Show(Playing);
        await settled;

        Assert.Equal(LyricsState.NotFound, feed.State);
        Assert.Null(feed.Result);
    }

    [Fact]
    public void Without_providers_stored_lyrics_are_shown_at_once_and_other_tracks_are_off()
    {
        var store = new LyricsStore(Db());
        store.Put(LyricsFeed.QueryOf(Playing), new LyricsResult("kept", null, "manual"), DateTimeOffset.UtcNow);
        using var feed = new LyricsFeed(new LyricsService(store, [], TimeSpan.FromDays(1)));

        feed.Show(Playing);
        Assert.Equal(LyricsState.Found, feed.State);

        feed.Show(Playing with { Track = Playing.Track with { Title = "Another" } });
        Assert.Equal(LyricsState.Off, feed.State);
    }

    [Fact]
    public async Task Enabling_online_lookup_swaps_the_service_and_searches_the_current_track()
    {
        var store = new LyricsStore(Db());
        LyricsFeed? feed = null;
        var provider = new Provider(_ => new LyricsResult("online", null, "test"));
        feed = new LyricsFeed(new LyricsService(store, [], TimeSpan.FromDays(1)), () => feed!.Replace(new LyricsService(store, [provider], TimeSpan.FromDays(1))));
        using var _ = feed;
        feed.Show(Playing);
        Assert.True(feed.CanEnableOnline);
        var settled = Settled(feed);

        feed.EnableOnline();

        await settled;
        Assert.Equal(LyricsState.Found, feed.State);
        Assert.False(feed.CanEnableOnline);
    }

    [Fact]
    public async Task With_only_local_sources_a_track_without_lyrics_offers_online_lookup()
    {
        var local = new EmbeddedLyricsProvider((_, _) => Task.FromResult<string?>(null));
        using var feed = new LyricsFeed(new LyricsService(new LyricsStore(Db()), [local], TimeSpan.FromDays(1)), () => { });
        var done = new TaskCompletionSource();
        feed.Changed += () =>
        {
            if (feed.State == LyricsState.Off)
            {
                done.TrySetResult();
            }
        };

        feed.Show(Playing);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(LyricsState.Off, feed.State);
        Assert.True(feed.CanEnableOnline);
    }

    [Fact]
    public async Task Embedded_lyrics_are_found_without_online_lookup()
    {
        var local = new EmbeddedLyricsProvider((_, _) => Task.FromResult<string?>("a line of real lyrics\nand another one here"));
        using var feed = new LyricsFeed(new LyricsService(new LyricsStore(Db()), [local], TimeSpan.FromDays(1)), () => { });
        var settled = Settled(feed);

        feed.Show(Playing);
        await settled;

        Assert.Equal(LyricsState.Found, feed.State);
        Assert.Equal("embedded", feed.Result!.Source);
    }

    [Fact]
    public void Nothing_playing_is_idle()
    {
        using var feed = new LyricsFeed(new LyricsService(new LyricsStore(Db()), [], TimeSpan.FromDays(1)));
        feed.Show(Playing);

        feed.Show(null);

        Assert.Equal(LyricsState.Idle, feed.State);
    }
}
