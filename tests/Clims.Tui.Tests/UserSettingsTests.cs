using System.Text.Json;
using Clims.Tui.Settings;
using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public class UserSettingsTests
{
    private static UserSettings Read(string json) => JsonSerializer.Deserialize<UserSettings>(json, SettingsJson.Options)!;

    [Fact]
    public void The_cover_cache_is_on_by_default_with_a_ttl_per_resolution()
    {
        var covers = Read("{}").Cache.Covers;

        Assert.True(covers.Enabled);
        Assert.Equal(30, covers.Normal.TtlDays);
        Assert.Equal(30, covers.Big.TtlDays);
        Assert.Equal(50, covers.Big.MaxEntries);
    }

    [Fact]
    public void The_cache_node_can_be_changed_in_the_file_and_survives_a_round_trip()
    {
        var settings = Read("""{"Cache":{"Covers":{"Enabled":false,"Normal":{"TtlDays":7},"Big":{"TtlDays":0}}}}""");

        Assert.False(settings.Cache.Covers.Enabled);
        Assert.Equal(7, settings.Cache.Covers.Normal.TtlDays);
        Assert.False(settings.Cache.Covers.Big.Active);
        Assert.Equal(settings.Cache, Read(JsonSerializer.Serialize(settings, SettingsJson.Options)).Cache);
    }

    [Fact]
    public void Lyrics_are_shown_when_stored_but_not_searched_online_by_default_and_a_miss_is_retried_after_a_day()
    {
        var lyrics = Read("""{"Servers":[{"Url":"http://h/"}]}""").Servers[0].Lyrics;

        Assert.True(lyrics.Enabled);
        Assert.False(lyrics.OnlineLookup);
        Assert.Equal(["lrclib"], lyrics.Providers);
        Assert.Equal(1, lyrics.MissRetryDays);
        Assert.Empty(lyrics.Files.PathMap);
    }

    [Fact]
    public void The_lyrics_node_can_be_changed_in_the_file_and_survives_a_round_trip()
    {
        var json = """{"Servers":[{"Url":"http://h/","Lyrics":{"OnlineLookup":true,"Providers":["lrclib","darklyrics"],"MissRetryDays":3}}]}""";
        var settings = Read(json);

        var back = Read(JsonSerializer.Serialize(settings, SettingsJson.Options)).Servers[0].Lyrics;
        Assert.True(back.OnlineLookup);
        Assert.Equal(["lrclib", "darklyrics"], back.Providers);
        Assert.Equal(3, back.MissRetryDays);
    }

    [Theory]
    [InlineData("Braille", WaveformStyle.Braille)]
    [InlineData("sixel", WaveformStyle.Sixel)]
    [InlineData("Blocks", WaveformStyle.Blocks)]
    public void The_waveform_style_is_read_by_name_in_any_case(string value, WaveformStyle expected) =>
        Assert.Equal(expected, Read("{\"UI\":{\"Waveform\":{\"Style\":\"" + value + "\"}}}").Ui.Waveform.Style);

    [Fact]
    public void A_missing_waveform_style_keeps_the_current_look()
    {
        Assert.Equal(WaveformStyle.Blocks, Read("""{"Servers":[]}""").Ui.Waveform.Style);
    }

    [Fact]
    public void WithServer_adds_a_server_named_after_its_host_and_makes_it_the_one_to_open()
    {
        var s = new UserSettings().WithServer("http://nas:9000/", "uuid-1");

        var server = Assert.Single(s.Servers);
        Assert.Equal("nas", server.Name);
        Assert.Equal("uuid-1", server.Uuid);
        Assert.Equal("uuid-1", s.Startup.Server);
        Assert.Same(server, s.ActiveServer);
    }

    [Fact]
    public void WithServer_changes_the_matching_entry_by_address_and_keeps_its_other_values()
    {
        var s = new UserSettings().WithServer("http://nas/", "u", e => e with { Username = "me", PlayerId = "p" })
            .WithServer("http://other/", "v")
            .WithServer("http://nas/");

        Assert.Equal(2, s.Servers.Count);
        Assert.Equal("me", s.Servers[0].Username);
        Assert.Equal("u", s.Servers[0].Uuid);
        Assert.Equal("u", s.Startup.Server);
    }

    [Fact]
    public void WithServer_finds_a_server_by_uuid_when_its_address_changed()
    {
        var s = new UserSettings().WithServer("http://old/", "u", e => e with { Username = "me" })
            .WithServer("http://new/", "u");

        var server = Assert.Single(s.Servers);
        Assert.Equal("http://new/", server.Url);
        Assert.Equal("me", server.Username);
    }
}
