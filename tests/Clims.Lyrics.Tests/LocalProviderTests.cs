namespace Clims.Lyrics.Tests;

public class LocalProviderTests
{
    private static LyricsQuery Q(string title, string? url = null, int? id = null) =>
        new("Vulture Industries", "Ghosts from the Past", title, TimeSpan.FromSeconds(200), TrackId: id, Url: url);

    [Fact]
    public void Plain_text_is_kept_and_junk_is_not_taken_for_lyrics()
    {
        Assert.Equal("one two\nthree four five six", LyricsText.From("one two\r\nthree four five six", "x")!.Plain);
        Assert.Null(LyricsText.From("XXX|", "x"));
        Assert.Null(LyricsText.From("   ", "x"));
        Assert.Null(LyricsText.From("1234567890123", "x"));
        Assert.Null(LyricsText.From(null, "x"));
    }

    [Fact]
    public void An_instrumental_note_is_an_instrumental()
    {
        Assert.True(LyricsText.From(" Instrumental", "x")!.Instrumental);
        Assert.True(LyricsText.From("[Instrumental]", "x")!.Instrumental);
    }

    [Fact]
    public void Text_with_timestamps_is_synced_and_its_plain_text_has_no_stamps()
    {
        var result = LyricsText.From("[ar:Someone]\n[00:01.00]first line\n[00:05.50]second line", "x")!;

        Assert.True(result.IsSynced);
        Assert.Equal("first line\nsecond line", result.Plain);
    }

    [Fact]
    public async Task Embedded_asks_the_server_for_the_track_and_returns_its_lyrics()
    {
        int? asked = null;
        var provider = new EmbeddedLyricsProvider((id, _) =>
        {
            asked = id;
            return Task.FromResult<string?>("[00:01.00]a long enough line\n[00:03.00]another line");
        });

        var result = await provider.FindAsync(Q("Deeper", id: 42), default);

        Assert.Equal(42, asked);
        Assert.True(result!.IsSynced);
        Assert.Equal("embedded", result.Source);
        Assert.False(provider.IsOnline);
    }

    [Fact]
    public async Task Embedded_has_nothing_to_say_without_a_track_id_or_with_junk_tags()
    {
        var calls = 0;
        var provider = new EmbeddedLyricsProvider((_, _) =>
        {
            calls++;
            return Task.FromResult<string?>("XXX|");
        });

        Assert.Null(await provider.FindAsync(Q("Deeper"), default));
        Assert.Equal(0, calls);
        Assert.Null(await provider.FindAsync(Q("Deeper", id: 1), default));
    }

    private sealed class Share : IDisposable
    {
        public Share()
        {
            Root = Path.Combine(Path.GetTempPath(), "clims-share-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "Vulture Industries", "Ghosts from the Past"));
        }

        public string Root { get; }

        public string Album => Path.Combine(Root, "Vulture Industries", "Ghosts from the Past");

        public SidecarLyricsProvider Provider => new(new Dictionary<string, string> { ["/mnt/Music"] = Root });

        public void Dispose() => Directory.Delete(Root, true);
    }

    private const string Url = "file:///mnt/Music/Vulture%20Industries/Ghosts%20from%20the%20Past/04%20-%20Deeper.mp3";

    [Fact]
    public async Task A_lrc_file_named_like_the_track_is_read_as_synced_lyrics()
    {
        using var share = new Share();
        File.WriteAllText(Path.Combine(share.Album, "04 - Deeper.lrc"), "[00:01.00]down we go now\n[00:04.00]deeper and deeper");

        var result = await share.Provider.FindAsync(Q("Deeper", Url), default);

        Assert.True(result!.IsSynced);
        Assert.Equal("sidecar", result.Source);
    }

    [Fact]
    public async Task A_txt_file_named_like_the_track_is_plain_lyrics_but_other_txt_files_are_not()
    {
        using var share = new Share();
        File.WriteAllText(Path.Combine(share.Album, "04 - Deeper.txt"), "down we go now\ndeeper and deeper");
        File.WriteAllText(Path.Combine(share.Album, "info.txt"), "Ripped from CD with a lot of care and attention");

        var result = await share.Provider.FindAsync(Q("Deeper", Url), default);
        Assert.False(result!.IsSynced);
        Assert.StartsWith("down we go", result.Plain);

        var other = "file:///mnt/Music/Vulture%20Industries/Ghosts%20from%20the%20Past/05%20-%20Other.mp3";
        Assert.Null(await share.Provider.FindAsync(Q("Other", other), default));
    }

    [Fact]
    public async Task A_lrc_with_another_name_is_used_when_it_contains_the_title_as_whole_words()
    {
        using var share = new Share();
        File.WriteAllText(Path.Combine(share.Album, "Vulture Industries - Deeper.lrc"), "[00:01.00]down we go now\n[00:04.00]deeper and deeper");

        Assert.NotNull(await share.Provider.FindAsync(Q("Deeper", Url), default));
        Assert.Null(await share.Provider.FindAsync(Q("Deep", Url.Replace("Deeper", "Deep", StringComparison.Ordinal)), default));
    }

    [Fact]
    public async Task Nothing_is_looked_up_without_a_matching_map_entry_or_an_address()
    {
        using var share = new Share();
        File.WriteAllText(Path.Combine(share.Album, "04 - Deeper.lrc"), "[00:01.00]down we go now\n[00:04.00]deeper and deeper");

        Assert.Null(await share.Provider.FindAsync(Q("Deeper", Url.Replace("/mnt/Music", "/srv/other", StringComparison.Ordinal)), default));
        Assert.Null(await share.Provider.FindAsync(Q("Deeper"), default));
        Assert.Null(await new SidecarLyricsProvider(new Dictionary<string, string>()).FindAsync(Q("Deeper", Url), default));
    }

    [Fact]
    public async Task A_missing_share_is_an_io_error_not_a_miss()
    {
        var gone = Path.Combine(Path.GetTempPath(), "clims-share-gone-" + Guid.NewGuid().ToString("N"));
        var provider = new SidecarLyricsProvider(new Dictionary<string, string> { ["/mnt/Music"] = gone });

        await Assert.ThrowsAsync<IOException>(() => provider.FindAsync(Q("Deeper", Url), default));
    }

    [Fact]
    public void The_longest_map_entry_wins_and_the_address_is_decoded()
    {
        var provider = new SidecarLyricsProvider(new Dictionary<string, string> { ["/mnt"] = "/a", ["/mnt/Music"] = "/b" });

        var folder = provider.FolderOf(Url, out var name);

        Assert.Equal(Path.Combine("/b", "Vulture Industries", "Ghosts from the Past"), folder);
        Assert.Equal("04 - Deeper", name);
    }

    [Fact]
    public async Task The_service_does_not_remember_an_unreachable_share_as_a_miss()
    {
        var gone = Path.Combine(Path.GetTempPath(), "clims-share-gone-" + Guid.NewGuid().ToString("N"));
        using var temp = new TempFolder();
        var sidecar = new SidecarLyricsProvider(new Dictionary<string, string> { ["/mnt/Music"] = gone });
        var store = new LyricsStore(temp.File("lyrics.db"));
        var service = new LyricsService(store, [sidecar], TimeSpan.FromDays(1));

        Assert.Null(await service.FindAsync(Q("Deeper", Url)));

        Assert.False(store.IsRecentMiss(Q("Deeper", Url).Key, DateTimeOffset.UtcNow, TimeSpan.FromDays(1), ["sidecar"]));
        Assert.False(service.CanSearchOnline);
        Assert.True(service.CanSearch);
    }
}
