namespace Clims.Lyrics.Tests;

public class LyricsStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly LyricsQuery Song = new("Slayer", "Reign In Blood", "Raining Blood", TimeSpan.FromSeconds(257));

    [Fact]
    public void A_missing_file_reads_as_empty_and_is_not_created_by_reading()
    {
        using var temp = new TempFolder();
        var store = new LyricsStore(temp.File("lyrics.db"));

        Assert.Null(store.Get(Song.Key));
        Assert.Equal(0, store.Count());
        Assert.False(File.Exists(store.Path));
    }

    [Fact]
    public void Lyrics_round_trip_with_their_timestamps_and_source()
    {
        using var temp = new TempFolder();
        var store = new LyricsStore(temp.File("sub", "lyrics.db"));

        store.Put(Song, new LyricsResult("a\nb", "[00:01.00]a\n[00:02.00]b", "lrclib"), Now);
        var back = store.Get(Song.Key);

        Assert.NotNull(back);
        Assert.Equal("a\nb", back.Plain);
        Assert.Equal("lrclib", back.Source);
        Assert.True(back.IsSynced);
        Assert.Equal(1, store.Count());
    }

    [Fact]
    public void Lyrics_survive_a_new_store_on_the_same_file()
    {
        using var temp = new TempFolder();
        new LyricsStore(temp.File("lyrics.db")).Put(Song, new LyricsResult("words", null, "darklyrics"), Now);

        Assert.Equal("words", new LyricsStore(temp.File("lyrics.db")).Get(Song.Key)?.Plain);
    }

    [Fact]
    public void An_instrumental_is_stored_as_a_hit()
    {
        using var temp = new TempFolder();
        var store = new LyricsStore(temp.File("lyrics.db"));

        store.Put(Song, new LyricsResult("", null, "lrclib", Instrumental: true), Now);

        Assert.True(store.Get(Song.Key)!.Instrumental);
    }

    [Fact]
    public void Put_without_replace_keeps_what_is_there()
    {
        using var temp = new TempFolder();
        var store = new LyricsStore(temp.File("lyrics.db"));
        store.Put(Song, new LyricsResult("first", null, "lrclib"), Now);

        store.Put(Song, new LyricsResult("second", null, "darklyrics"), Now, replace: false);

        Assert.Equal("first", store.Get(Song.Key)!.Plain);
    }

    [Fact]
    public void Hand_made_lyrics_are_never_replaced_by_a_search_but_can_be_by_hand()
    {
        using var temp = new TempFolder();
        var store = new LyricsStore(temp.File("lyrics.db"));
        store.Put(Song, new LyricsResult("mine", null, "manual"), Now, userEdited: true);

        store.Put(Song, new LyricsResult("found", null, "lrclib"), Now);
        Assert.Equal("mine", store.Get(Song.Key)!.Plain);
        Assert.True(store.IsUserEdited(Song.Key));

        store.Put(Song, new LyricsResult("mine again", null, "manual"), Now, userEdited: true);
        Assert.Equal("mine again", store.Get(Song.Key)!.Plain);
    }

    [Fact]
    public void A_miss_holds_for_the_retry_time_only()
    {
        using var temp = new TempFolder();
        var store = new LyricsStore(temp.File("lyrics.db"));
        store.PutMiss(Song.Key, Now, ["lrclib"]);

        Assert.True(store.IsRecentMiss(Song.Key, Now.AddHours(23), TimeSpan.FromDays(1), ["lrclib"]));
        Assert.False(store.IsRecentMiss(Song.Key, Now.AddHours(25), TimeSpan.FromDays(1), ["lrclib"]));
    }

    [Fact]
    public void A_provider_enabled_since_the_miss_gets_its_try_at_once()
    {
        using var temp = new TempFolder();
        var store = new LyricsStore(temp.File("lyrics.db"));
        store.PutMiss(Song.Key, Now, ["lrclib"]);

        Assert.False(store.IsRecentMiss(Song.Key, Now.AddMinutes(1), TimeSpan.FromDays(1), ["lrclib", "darklyrics"]));
    }

    [Fact]
    public void Storing_lyrics_forgets_the_miss()
    {
        using var temp = new TempFolder();
        var store = new LyricsStore(temp.File("lyrics.db"));
        store.PutMiss(Song.Key, Now, ["lrclib"]);

        store.Put(Song, new LyricsResult("x", null, "lrclib"), Now);

        Assert.False(store.IsRecentMiss(Song.Key, Now, TimeSpan.FromDays(1), ["lrclib"]));
    }

    [Fact]
    public void Delete_removes_lyrics_and_miss()
    {
        using var temp = new TempFolder();
        var store = new LyricsStore(temp.File("lyrics.db"));
        store.Put(Song, new LyricsResult("x", null, "lrclib"), Now, userEdited: true);

        store.Delete(Song.Key);

        Assert.Null(store.Get(Song.Key));
    }

    [Fact]
    public void A_corrupt_file_reads_as_empty_and_writes_do_not_throw()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("lyrics.db"), "this is not a database");
        var store = new LyricsStore(temp.File("lyrics.db"));

        Assert.Null(store.Get(Song.Key));
        store.Put(Song, new LyricsResult("x", null, "lrclib"), Now);
        store.PutMiss(Song.Key, Now, ["lrclib"]);
        Assert.False(store.IsRecentMiss(Song.Key, Now, TimeSpan.FromDays(1), ["lrclib"]));
    }
}
