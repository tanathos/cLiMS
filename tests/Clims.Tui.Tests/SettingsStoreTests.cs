using Clims.Tui.Settings;
using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "clims-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private SettingsStore Store() => new(_folder);

    private void Put(string name, string text)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, name), text);
    }

    [Fact]
    public void No_file_gives_the_defaults_and_writes_nothing()
    {
        var s = Store().Load();

        Assert.Equal(UserSettings.CurrentVersion, s.Version);
        Assert.False(File.Exists(Store().FilePath));
    }

    [Fact]
    public void Saved_settings_are_indented_with_enums_as_names_and_read_back_equal()
    {
        var store = Store();
        var settings = new UserSettings
        {
            Servers = [new ServerSettings { Name = "NAS", Url = "http://nas/", Uuid = "u" }],
            Startup = new StartupSettings { Server = "u", View = StartupView.Recent },
            Ui = new UiSettings { Waveform = new WaveformSettings { Style = WaveformStyle.Braille } },
        };

        store.Save(settings);

        var text = File.ReadAllText(store.FilePath);
        Assert.Contains("\n", text);
        Assert.Contains("  \"Version\": 2", text);
        Assert.Contains("\"UI\"", text);
        Assert.Contains("\"Style\": \"Braille\"", text);
        Assert.Contains("\"View\": \"Recent\"", text);
        Assert.DoesNotContain("\"Size\"", text[text.IndexOf("\"Footer\"", StringComparison.Ordinal)..text.IndexOf("\"Library\"", StringComparison.Ordinal)]);

        var back = store.Load();
        Assert.Equal(StartupView.Recent, back.Startup.View);
        Assert.Equal(WaveformStyle.Braille, back.Ui.Waveform.Style);
        Assert.Equal("NAS", back.ActiveServer?.Name);
        Assert.Equal(CoverSize.Medium, back.Ui.Covers.Library.Size);
        Assert.False(File.Exists(Path.Combine(_folder, "settings.v1.json")));
    }

    [Fact]
    public void A_partial_version_2_file_takes_defaults_for_the_rest()
    {
        Put("settings.json", "{ \"Version\": 2, \"UI\": { \"Waveform\": { \"Style\": \"sixel\" } } }");

        var s = Store().Load();

        Assert.Equal(WaveformStyle.Sixel, s.Ui.Waveform.Style);
        Assert.True(s.Ui.Waveform.Enabled);
        Assert.Equal(CoverMode.Quadrant, s.Ui.Covers.SixelFallback);
    }

    [Fact]
    public void A_version_1_file_is_kept_as_settings_v1_and_replaced_by_version_2()
    {
        const string old = "{\"LmsUrl\":\"http://nas:9000/\",\"ServerUuid\":\"u\",\"Waveform\":\"Braille\"}";
        Put("settings.json", old);

        var s = Store().Load();

        Assert.Equal("http://nas:9000/", s.ActiveServer?.Url);
        Assert.Equal(old, File.ReadAllText(Path.Combine(_folder, "settings.v1.json")));
        var written = File.ReadAllText(Path.Combine(_folder, "settings.json"));
        Assert.Contains("\"Version\": 2", written);
        Assert.DoesNotContain("LmsUrl", written);
        Assert.False(SettingsMigration.IsOldVersion(written));
    }

    [Fact]
    public void An_existing_backup_is_not_overwritten()
    {
        Put("settings.v1.json", "first");
        Put("settings.json", "{\"LmsUrl\":\"http://nas/\"}");

        Store().Load();

        Assert.Equal("first", File.ReadAllText(Path.Combine(_folder, "settings.v1.json")));
        Assert.Contains("LmsUrl", File.ReadAllText(Path.Combine(_folder, "settings.v1.1.json")));
        Assert.Equal(Path.Combine(_folder, "settings.v1.2.json"), Store().BackupPath());
    }

    [Fact]
    public void An_unreadable_file_is_kept_as_the_backup_and_the_defaults_are_used()
    {
        Put("settings.json", "{ this is not json");

        var s = Store().Load();

        Assert.Empty(s.Servers);
        Assert.Equal("{ this is not json", File.ReadAllText(Path.Combine(_folder, "settings.v1.json")));
        Assert.False(File.Exists(Path.Combine(_folder, "settings.json")));
    }

    [Fact]
    public void Migration_runs_once()
    {
        Put("settings.json", "{\"LmsUrl\":\"http://nas/\"}");
        var store = Store();

        store.Load();
        var second = store.Load();

        Assert.Equal("http://nas/", second.ActiveServer?.Url);
        Assert.Single(Directory.GetFiles(_folder, "settings.v1*.json"));
    }
}
