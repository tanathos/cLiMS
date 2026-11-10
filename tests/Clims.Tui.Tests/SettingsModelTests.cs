using Clims.Tui.Settings;
using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public sealed class SettingsModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "clims-model-" + Guid.NewGuid().ToString("N"));
    private readonly SettingsStore _store;

    public SettingsModelTests() => _store = new SettingsStore(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private SettingsModel Model(string? connectedUrl = null, params string[] servers)
    {
        var settings = new UserSettings();
        foreach (var url in servers)
        {
            settings = settings.WithServer(url, "uuid-" + new Uri(url).Host);
        }

        _store.Update(_ => settings);
        return new SettingsModel(_store, connectedUrl);
    }

    private static SettingsModel Open(SettingsModel model, string page, string row)
    {
        model.SetPage(model.Pages.Select(p => p.Title).ToList().IndexOf(page));
        model.MoveTo(model.Items.Select(i => i.Label).ToList().IndexOf(row));
        return model;
    }

    [Fact]
    public void The_pages_are_servers_ui_lyrics_cache_and_startup()
    {
        Assert.Equal(["Servers", "UI", "Lyrics", "Cache", "Startup"], Model().Pages.Select(p => p.Title));
    }

    [Fact]
    public void A_toggle_flips_and_is_saved_at_once_and_a_restart_is_flagged()
    {
        var model = Open(Model(), "UI", "Waveform");
        UserSettings? seen = null;
        model.Changed += s => seen = s;

        model.Change(1);

        Assert.False(_store.Current.Ui.Waveform.Enabled);
        Assert.False(seen!.Ui.Waveform.Enabled);
        Assert.True(model.RestartNeeded);
        Assert.Contains("\"Enabled\": false", File.ReadAllText(_store.FilePath));
    }

    [Fact]
    public void A_choice_goes_round_in_both_directions_and_applies_live_things_without_a_restart()
    {
        var model = Open(Model(), "UI", "Waveform style");

        model.Change(1);
        Assert.Equal(WaveformStyle.Braille, _store.Current.Ui.Waveform.Style);
        model.Change(-1);
        model.Change(-1);
        Assert.Equal(WaveformStyle.Sixel, _store.Current.Ui.Waveform.Style);
        model.Activate();
        Assert.Equal(WaveformStyle.Blocks, _store.Current.Ui.Waveform.Style);
        Assert.False(model.RestartNeeded);
    }

    [Fact]
    public void Cover_styles_and_the_library_size_are_set_per_place()
    {
        var model = Open(Model(), "UI", "Footer cover");
        model.Change(1);
        Open(model, "UI", "Library cover size");
        model.Change(1);

        Assert.Equal(CoverMode.HalfBlock, _store.Current.Ui.Covers.Footer.Style);
        Assert.Equal(CoverMode.Sixel, _store.Current.Ui.Covers.Library.Style);
        Assert.Equal(CoverSize.Large, _store.Current.Ui.Covers.Library.Size);
    }

    [Fact]
    public void A_number_steps_within_its_limits()
    {
        var model = Open(Model(), "Cache", "Covers: days kept");

        model.Change(-1);
        model.Change(-1);
        Assert.Equal(20, _store.Current.Cache.Covers.Normal.TtlDays);
        for (var i = 0; i < 10; i++)
        {
            model.Change(-1);
        }

        Assert.Equal(0, _store.Current.Cache.Covers.Normal.TtlDays);
        Assert.Equal("not cached", model.Current!.Value(_store.Current));
    }

    [Fact]
    public void The_lyrics_page_has_nothing_to_change_until_there_is_a_server()
    {
        var model = Model();
        model.SetPage(2);

        Assert.All(model.Items, i => Assert.Equal(SettingKind.Info, i.Kind));
    }

    [Fact]
    public void Lyrics_settings_change_the_server_that_opens_at_start_and_providers_stay_in_order()
    {
        var model = Open(Model(null, "http://nas:9000/"), "Lyrics", "Provider lyricsovh");

        model.Change(1);
        Open(model, "Lyrics", "Provider darklyrics");
        model.Change(1);
        Open(model, "Lyrics", "Provider lrclib");
        model.Change(1);
        Open(model, "Lyrics", "Search online");
        model.Change(1);

        var lyrics = _store.Current.ActiveServer!.Lyrics;
        Assert.Equal(["darklyrics", "lyricsovh"], lyrics.Providers);
        Assert.True(lyrics.OnlineLookup);
        Assert.Single(_store.Current.Servers);
    }

    [Fact]
    public void Selection_skips_information_rows()
    {
        var model = Open(Model(null, "http://nas/"), "Lyrics", "Lyrics");

        model.Move(-1);

        Assert.Equal("Lyrics", model.Current!.Label); // the row above is information: the selection stays
    }

    [Fact]
    public void A_server_can_be_renamed_but_not_to_nothing()
    {
        var model = Model(null, "http://nas/");
        model.SetPage(0);

        model.BeginEdit();
        Assert.True(model.Editing);
        Assert.Equal("nas", model.Buffer);
        for (var i = 0; i < 3; i++)
        {
            model.Backspace();
        }

        model.CommitEdit();
        Assert.True(model.Editing);
        Assert.Equal("nas", _store.Current.Servers[0].Name);

        model.Type("Home NAS");
        model.CommitEdit();
        Assert.False(model.Editing);
        Assert.Equal("Home NAS", _store.Current.Servers[0].Name);
    }

    [Fact]
    public void Enter_on_a_server_asks_to_connect_to_it_and_on_the_last_row_to_add_one()
    {
        var model = Model(null, "http://nas/", "http://other/");
        string? key = null;
        var added = false;
        model.SwitchRequested += k => key = k;
        model.AddRequested += () => added = true;
        model.SetPage(0);

        model.MoveTo(1);
        model.Activate();
        model.MoveTo(2);
        model.Activate();

        Assert.Equal("uuid-other", key);
        Assert.True(added);
    }

    [Fact]
    public void U_makes_a_server_the_one_to_open_at_start()
    {
        var model = Model(null, "http://nas/", "http://other/");
        model.SetPage(0);
        Assert.Equal("uuid-other", _store.Current.Startup.Server);

        model.MoveTo(0);
        model.UseAtStart();

        Assert.Equal("uuid-nas", _store.Current.Startup.Server);
    }

    [Fact]
    public void Removing_needs_a_confirmation_and_the_server_in_use_cannot_be_removed()
    {
        var model = Model("http://nas/", "http://nas/", "http://other/");
        model.SetPage(0);

        model.MoveTo(0);
        model.BeginRemove();
        Assert.False(model.ConfirmingRemove);
        Assert.Equal(2, _store.Current.Servers.Count);

        model.MoveTo(1);
        model.BeginRemove();
        Assert.True(model.ConfirmingRemove);
        model.CancelRemove();
        Assert.Equal(2, _store.Current.Servers.Count);

        model.BeginRemove();
        model.ConfirmRemove();
        Assert.Equal("http://nas/", Assert.Single(_store.Current.Servers).Url);
        Assert.Equal("uuid-nas", _store.Current.Startup.Server);
    }

    [Fact]
    public void Removing_the_server_that_opens_at_start_hands_that_to_the_first_one_left()
    {
        var model = Model(null, "http://nas/", "http://other/");
        model.SetPage(0);
        model.MoveTo(1);

        model.BeginRemove();
        model.ConfirmRemove();

        Assert.Equal("uuid-nas", _store.Current.Startup.Server);
    }

    [Fact]
    public void A_stored_password_is_never_among_the_values_shown()
    {
        var model = Model(null, "http://nas/");
        _store.Update(s => s.WithServer("http://nas/", change: x => x with { Username = "me", ProtectedPassword = "SECRET-BLOB" }));

        var shown = model.Pages.SelectMany(p => p.Items).Select(i => i.Label + i.Value(_store.Current) + i.Hint);

        Assert.DoesNotContain(shown, text => text.Contains("SECRET-BLOB", StringComparison.Ordinal));
    }
}
