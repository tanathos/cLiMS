using System.Text.Json;
using Clims.Tui.Settings;
using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public class SettingsMigrationTests
{
    private const string FullV1 = """
        {
          "LmsUrl": "http://192.168.1.100:9000/",
          "ServerUuid": "uuid-1",
          "PlayerId": "aa:bb:cc:dd:ee:ff",
          "Username": "eros",
          "ProtectedPassword": "secret",
          "CoverCache": { "Enabled": false, "Normal": { "TtlDays": 7, "MaxEntries": 0 }, "Big": { "TtlDays": 3, "MaxEntries": 20 } },
          "Lyrics": {
            "Enabled": true, "OnlineLookup": true, "Providers": ["lrclib", "lyricsovh"],
            "MusicPathMap": { "/mnt/Music": "\\\\nas\\Music" },
            "MusicShareUser": "NAS\\eros", "MusicShareProtectedPassword": "enc", "MissRetryDays": 5
          },
          "Waveform": "Sixel"
        }
        """;

    [Fact]
    public void A_file_without_a_version_is_old_and_version_2_is_not()
    {
        Assert.True(SettingsMigration.IsOldVersion(FullV1));
        Assert.True(SettingsMigration.IsOldVersion("{}"));
        Assert.False(SettingsMigration.IsOldVersion("{\"Version\":2}"));
    }

    [Fact]
    public void The_flat_keys_become_one_server_entry()
    {
        var s = SettingsMigration.FromV1(FullV1);

        var server = Assert.Single(s.Servers);
        Assert.Equal("192.168.1.100", server.Name);
        Assert.Equal("http://192.168.1.100:9000/", server.Url);
        Assert.Equal("uuid-1", server.Uuid);
        Assert.Equal("eros", server.Username);
        Assert.Equal("secret", server.ProtectedPassword);
        Assert.Equal("aa:bb:cc:dd:ee:ff", server.PlayerId);
        Assert.Equal("uuid-1", s.Startup.Server);
        Assert.Same(server, s.ActiveServer);
    }

    [Fact]
    public void Lyrics_move_into_the_server_with_the_share_keys_grouped_under_Files()
    {
        var lyrics = SettingsMigration.FromV1(FullV1).Servers[0].Lyrics;

        Assert.True(lyrics.OnlineLookup);
        Assert.Equal(["lrclib", "lyricsovh"], lyrics.Providers);
        Assert.Equal(5, lyrics.MissRetryDays);
        Assert.Equal("\\\\nas\\Music", lyrics.Files.PathMap["/mnt/Music"]);
        Assert.Equal("NAS\\eros", lyrics.Files.ShareUser);
        Assert.Equal("enc", lyrics.Files.ProtectedSharePassword);
    }

    [Fact]
    public void The_cover_cache_and_waveform_move_to_their_new_nodes()
    {
        var s = SettingsMigration.FromV1(FullV1);

        Assert.False(s.Cache.Covers.Enabled);
        Assert.Equal(7, s.Cache.Covers.Normal.TtlDays);
        Assert.Equal(20, s.Cache.Covers.Big.MaxEntries);
        Assert.Equal(WaveformStyle.Sixel, s.Ui.Waveform.Style);
    }

    [Fact]
    public void A_plain_share_password_is_encrypted_where_possible_and_never_kept_beside_its_encrypted_form()
    {
        var s = SettingsMigration.FromV1("{\"LmsUrl\":\"http://x/\",\"Lyrics\":{\"MusicSharePassword\":\"hunter2\"}}");
        var files = s.Servers[0].Lyrics.Files;

        if (SecretStore.CanStore)
        {
            Assert.Null(files.SharePassword);
            Assert.Equal("hunter2", SecretStore.Unprotect(files.ProtectedSharePassword));
        }
        else
        {
            Assert.Equal("hunter2", files.SharePassword);
        }
    }

    [Fact]
    public void Missing_and_unknown_values_take_the_defaults()
    {
        var s = SettingsMigration.FromV1("{\"Waveform\":\"nonsense\"}");

        Assert.Empty(s.Servers);
        Assert.Null(s.ActiveServer);
        Assert.Equal(WaveformStyle.Blocks, s.Ui.Waveform.Style);
        Assert.True(s.Cache.Covers.Enabled);
        Assert.True(s.Startup.AutoConnect);
    }

    [Fact]
    public void Text_that_is_not_an_object_is_rejected()
    {
        Assert.ThrowsAny<JsonException>(() => SettingsMigration.FromV1("[1,2]"));
        Assert.ThrowsAny<JsonException>(() => SettingsMigration.FromV1("{ nope"));
    }
}
