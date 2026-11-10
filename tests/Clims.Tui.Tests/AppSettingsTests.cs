using Clims.Catalog;
using Clims.Tui;
using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

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

    [Fact]
    public void The_cover_cache_is_on_by_default_with_a_ttl_per_resolution()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("""{"LmsUrl":"http://h/"}""")!;

        Assert.True(settings.CoverCache.Enabled);
        Assert.Equal(30, settings.CoverCache.Normal.TtlDays);
        Assert.Equal(30, settings.CoverCache.Big.TtlDays);
        Assert.Equal(50, settings.CoverCache.Big.MaxEntries);
    }

    [Fact]
    public void The_cover_cache_node_can_be_changed_in_the_file()
    {
        var json = """{"CoverCache":{"Enabled":false,"Normal":{"TtlDays":7},"Big":{"TtlDays":0}}}""";
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.False(settings.CoverCache.Enabled);
        Assert.Equal(7, settings.CoverCache.Normal.TtlDays);
        Assert.False(settings.CoverCache.Big.Active);
    }

    [Fact]
    public void The_cover_cache_node_survives_a_round_trip()
    {
        var saved = new AppSettings("http://h/") { CoverCache = new CoverCacheSettings { Normal = new CoverCacheTier(5, 100) } };
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(saved))!;

        Assert.Equal(saved.CoverCache, back.CoverCache);
    }

    [Fact]
    public void Lyrics_are_shown_when_stored_but_not_searched_online_by_default_and_a_miss_is_retried_after_a_day()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("""{"LmsUrl":"http://h/"}""")!;

        Assert.True(settings.Lyrics.Enabled);
        Assert.False(settings.Lyrics.OnlineLookup);
        Assert.Equal(["lrclib"], settings.Lyrics.Providers);
        Assert.Equal(1, settings.Lyrics.MissRetryDays);
    }

    [Fact]
    public void The_lyrics_node_can_be_changed_in_the_file_and_survives_a_round_trip()
    {
        var json = """{"Lyrics":{"OnlineLookup":true,"Providers":["lrclib","darklyrics"],"MissRetryDays":3}}""";
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.True(settings.Lyrics.OnlineLookup);
        Assert.Equal(["lrclib", "darklyrics"], settings.Lyrics.Providers);
        Assert.Equal(3, settings.Lyrics.MissRetryDays);

        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        Assert.Equal(settings.Lyrics.Providers, back.Lyrics.Providers);
        Assert.True(back.Lyrics.OnlineLookup);
    }
}
