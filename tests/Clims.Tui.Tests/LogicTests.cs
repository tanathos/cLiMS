using Clims.Catalog;
using Clims.Tui;
using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public class SyncStatusTextTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Fact]
    public void Not_synced() =>
        Assert.Equal(("○", "Not synced"), Pair(new SyncStatus(SyncState.NotSynced, false, null, null, null)));

    [Fact]
    public void Syncing_says_live_search_only_and_shows_progress() =>
        Assert.Equal(
            ("◐", "Sync in progress, live search only · artists ✓ albums ✓ tracks 42%"),
            Pair(new SyncStatus(SyncState.Syncing, true, "artists ✓ albums ✓ tracks 42%", null, null)));

    [Fact]
    public void Syncing_without_progress_text_has_no_dangling_separator() =>
        Assert.Equal("Sync in progress, live search only", Pair(new SyncStatus(SyncState.Syncing, false, null, null, null)).Label);

    [Fact]
    public void Synced_shows_the_age() =>
        Assert.Equal(("●", "Synced (2 h ago)"), Pair(new SyncStatus(SyncState.Synced, true, null, Now.AddHours(-2), null)));

    [Fact]
    public void Server_changed() =>
        Assert.Equal(("◔", "Server changed, resync pending"), Pair(new SyncStatus(SyncState.ServerChanged, true, null, Now, "x")));

    [Theory]
    [InlineData(true, "Offline (using local index)")]
    [InlineData(false, "Offline")]
    public void Offline_mentions_the_local_index_only_when_there_is_one(bool hasIndex, string expected) =>
        Assert.Equal(("✕", expected), Pair(new SyncStatus(SyncState.Offline, hasIndex, null, null, null)));

    [Fact]
    public void Demo() =>
        Assert.Equal(("◇", "Demo data (no server)"), Pair(new SyncStatus(SyncState.Demo, true, null, null, null)));

    [Fact]
    public void Every_state_has_a_distinct_symbol()
    {
        var symbols = Enum.GetValues<SyncState>().Select(s => HeaderBar.Describe(new SyncStatus(s, true, null, Now, null), Now).Symbol);
        Assert.Equal(6, symbols.Distinct().Count());
    }

    [Theory]
    [InlineData(5, "just now")]
    [InlineData(90, "1 min ago")]
    [InlineData(3000, "50 min ago")]
    [InlineData(7200, "2 h ago")]
    [InlineData(86400 * 3, "3 d ago")]
    public void Age_is_compact(int secondsAgo, string expected) =>
        Assert.Equal(expected, HeaderBar.Age(Now.AddSeconds(-secondsAgo), Now));

    [Fact]
    public void Unknown_age() => Assert.Equal("unknown age", HeaderBar.Age(null, Now));

    private static (string Symbol, string Label) Pair(SyncStatus s)
    {
        var (symbol, label, _) = HeaderBar.Describe(s, Now);
        return (symbol, label);
    }
}

public class AppSettingsTests
{
    [Theory]
    [InlineData("192.168.1.20", "http://192.168.1.20/")]
    [InlineData("192.168.1.20:9000", "http://192.168.1.20:9000/")]
    [InlineData("http://192.168.1.20:9000", "http://192.168.1.20:9000/")]
    [InlineData("  http://lms.local:9000/  ", "http://lms.local:9000/")]
    [InlineData("https://lms.example.org/lms", "https://lms.example.org/lms/")]
    public void Addresses_are_normalised_to_a_slash_terminated_http_uri(string input, string expected) =>
        Assert.Equal(expected, AppSettings.NormalizeUrl(input)!.ToString());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://host/")]
    public void Unusable_addresses_give_null(string? input) => Assert.Null(AppSettings.NormalizeUrl(input));
}

public class TabStripPromptTests
{
    // Answering needs no terminal: Answer/Choices are plain state on the view.
    [Fact]
    public void A_prompt_is_answered_by_key_case_insensitively_and_then_cleared()
    {
        var strip = new TabStrip("A", "B");
        var ran = new List<string>();
        strip.ShowPrompt("Sync?", new PromptChoice('Y', "Yes", () => ran.Add("yes")), new PromptChoice('L', "Later", () => ran.Add("later")));

        Assert.Equal(2, strip.Choices.Count);
        Assert.True(strip.Answer('l'));
        Assert.Equal(new[] { "later" }, ran);
        Assert.Empty(strip.Choices);
        Assert.False(strip.Answer('y')); // the question is gone
    }

    [Fact]
    public void An_unknown_key_leaves_the_prompt_open()
    {
        var strip = new TabStrip("A");
        strip.ShowPrompt("Sync?", new PromptChoice('Y', "Yes", () => { }));
        Assert.False(strip.Answer('x'));
        Assert.Single(strip.Choices);
    }

    [Fact]
    public void A_notice_never_replaces_a_pending_question()
    {
        var strip = new TabStrip("A");
        strip.ShowPrompt("Sync?", new PromptChoice('Y', "Yes", () => { }));
        strip.ShowNotice("something else");
        Assert.Single(strip.Choices);
    }
}
