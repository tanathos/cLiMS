namespace Clims.Lyrics.Tests;

public class LyricsServiceTests
{
    private static readonly LyricsQuery Song = new("Slayer", "Reign In Blood", "Raining Blood", TimeSpan.FromSeconds(257));
    private DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private LyricsService Service(TempFolder temp, params ILyricsProvider[] providers) =>
        new(new LyricsStore(temp.File("lyrics.db")), providers, TimeSpan.FromDays(1), () => _now);

    private static LyricsResult Found(string text, string source = "stub") => new(text, null, source);

    [Fact]
    public async Task A_stored_lyric_is_returned_without_asking_any_provider()
    {
        using var temp = new TempFolder();
        var provider = new StubProvider("a", _ => Found("from provider"));
        var service = Service(temp, provider);
        await service.FindAsync(Song);

        var again = await service.FindAsync(Song);

        Assert.Equal("from provider", again!.Plain);
        Assert.Equal(1, provider.Calls);
    }


    [Fact]
    public async Task The_log_tells_what_each_provider_answered_and_which_result_was_taken()
    {
        using var temp = new TempFolder();
        var lines = new List<string>();
        var service = new LyricsService(new LyricsStore(temp.File("lyrics.db")), [new StubProvider("first", _ => null), new StubProvider("second", _ => Found("two", "second"))], TimeSpan.FromDays(1), () => _now, lines.Add);

        await service.FindAsync(Song);
        await service.FindAsync(Song);

        Assert.Contains(lines, l => l.Contains("searching first, second"));
        Assert.Contains(lines, l => l.Contains("first has no match"));
        Assert.Contains(lines, l => l.Contains("second matched, selected (plain text)"));
        Assert.Contains(lines, l => l.Contains("stored copy used (source second)"));
    }

    [Fact]
    public async Task Asking_for_another_provider_skips_the_one_that_gave_the_stored_lyrics_and_walks_on_from_it()
    {
        using var temp = new TempFolder();
        var a = new StubProvider("a", _ => Found("from a", "a"));
        var b = new StubProvider("b", _ => null);
        var c = new StubProvider("c", _ => Found("from c", "c"));
        var service = Service(temp, a, c);
        Assert.Equal("a", (await service.FindAsync(Song))!.Source);

        var again = await Service(temp, a, b, c).FindAsync(Song, force: true, otherThan: "a");

        Assert.Equal("c", again!.Source);
        Assert.Equal(1, a.Calls);
        Assert.Equal(1, b.Calls);
    }

    [Fact]
    public async Task When_no_other_provider_has_it_the_stored_lyrics_are_kept()
    {
        using var temp = new TempFolder();
        var a = new StubProvider("a", _ => Found("from a", "a"));
        var b = new StubProvider("b", _ => null);
        var service = Service(temp, a, b);
        await service.FindAsync(Song);

        var again = await service.FindAsync(Song, force: true, otherThan: "a");

        Assert.Equal("a", again!.Source);
        Assert.Equal(1, a.Calls);
    }
    [Fact]
    public async Task Providers_are_asked_in_order_and_the_first_hit_wins()
    {
        using var temp = new TempFolder();
        var first = new StubProvider("first", _ => null);
        var second = new StubProvider("second", _ => Found("two", "second"));
        var third = new StubProvider("third", _ => Found("three", "third"));

        var result = await Service(temp, first, second, third).FindAsync(Song);

        Assert.Equal("two", result!.Plain);
        Assert.Equal(0, third.Calls);
    }

    [Fact]
    public async Task A_miss_is_not_searched_again_until_the_retry_time_has_passed()
    {
        using var temp = new TempFolder();
        var provider = new StubProvider("a", _ => null);
        var service = Service(temp, provider);

        Assert.Null(await service.FindAsync(Song));
        _now = _now.AddHours(23);
        Assert.Null(await service.FindAsync(Song));
        Assert.Equal(1, provider.Calls);

        _now = _now.AddHours(2);
        await service.FindAsync(Song);
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task A_provider_that_cannot_be_reached_leaves_no_miss_behind()
    {
        using var temp = new TempFolder();
        var down = true;
        var provider = new StubProvider("a", _ => down ? throw new HttpRequestException("offline") : Found("back"));
        var service = Service(temp, provider);

        Assert.Null(await service.FindAsync(Song));
        down = false;

        Assert.Equal("back", (await service.FindAsync(Song))!.Plain);
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task The_next_provider_is_tried_when_one_is_unreachable()
    {
        using var temp = new TempFolder();
        var down = new StubProvider("down", _ => throw new HttpRequestException("offline"));
        var up = new StubProvider("up", _ => Found("ok"));

        Assert.Equal("ok", (await Service(temp, down, up).FindAsync(Song))!.Plain);
    }

    [Fact]
    public async Task Other_songs_found_on_the_way_are_stored_without_being_searched()
    {
        using var temp = new TempFolder();
        var other = new LyricsQuery("Slayer", "Reign In Blood", "Angel Of Death", TimeSpan.Zero);
        var provider = new StubProvider("album", _ => new LyricsResult("rain", null, "album") { Siblings = [(other, new LyricsResult("angel", null, "album"))] });
        var service = Service(temp, provider);
        await service.FindAsync(Song);

        var result = await service.FindAsync(other with { Duration = TimeSpan.FromSeconds(300) });

        Assert.Equal("angel", result!.Plain);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task A_new_provider_is_tried_even_if_the_old_ones_missed_a_moment_ago()
    {
        using var temp = new TempFolder();
        var store = new LyricsStore(temp.File("lyrics.db"));
        var old = new StubProvider("old", _ => null);
        await new LyricsService(store, [old], TimeSpan.FromDays(1), () => _now).FindAsync(Song);
        var added = new StubProvider("new", _ => Found("hit", "new"));

        var result = await new LyricsService(store, [old, added], TimeSpan.FromDays(1), () => _now).FindAsync(Song);

        Assert.Equal("hit", result!.Plain);
    }

    [Fact]
    public async Task Force_searches_again_and_keeps_the_old_lyrics_when_nothing_better_is_found()
    {
        using var temp = new TempFolder();
        var answers = new Queue<LyricsResult?>([Found("plain"), null, new LyricsResult("better", "[00:01.00]better", "stub")]);
        var service = Service(temp, new StubProvider("a", _ => answers.Dequeue()));
        await service.FindAsync(Song);

        Assert.Equal("plain", (await service.FindAsync(Song, force: true))!.Plain);
        Assert.True((await service.FindAsync(Song, force: true))!.IsSynced);
    }

    [Fact]
    public async Task Hand_made_lyrics_are_never_searched_again()
    {
        using var temp = new TempFolder();
        var store = new LyricsStore(temp.File("lyrics.db"));
        store.Put(Song, new LyricsResult("mine", null, "manual"), _now, userEdited: true);
        var provider = new StubProvider("a", _ => Found("other"));

        var result = await new LyricsService(store, [provider], TimeSpan.FromDays(1), () => _now).FindAsync(Song, force: true);

        Assert.Equal("mine", result!.Plain);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Without_providers_only_the_store_is_used_and_no_miss_is_recorded()
    {
        using var temp = new TempFolder();
        var service = Service(temp);

        Assert.False(service.CanSearch);
        Assert.Null(await service.FindAsync(Song));
    }
}
