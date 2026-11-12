using System.Text.Json;
using Clims.Tui.Settings;
using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public sealed class ThemeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "clims-theme-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Theme.Apply(new ThemeSettings());
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Every_theme_has_a_colour_for_every_slot_and_a_unique_name()
    {
        Assert.Equal(Themes.All.Count, Themes.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var theme in Themes.All)
        {
            foreach (var slot in Enum.GetValues<ThemeSlot>())
            {
                _ = theme.Palette[slot];
            }
        }
    }

    [Fact]
    public void The_default_theme_keeps_the_colours_cLiMS_always_had()
    {
        var p = Themes.Default.Palette;
        Assert.Equal(Theme.Rgb(24, 26, 31), p[ThemeSlot.Bg]);
        Assert.Equal(Theme.Rgb(110, 190, 255), p[ThemeSlot.Accent]);
        Assert.Equal(Theme.Rgb(160, 166, 180), p[ThemeSlot.HoverBg]);
    }

    [Fact]
    public void An_unknown_theme_name_gives_the_default()
    {
        Assert.Same(Themes.Default, Themes.Find("nope"));
        Assert.Same(Themes.Nord, Themes.Find("nord"));
    }

    [Fact]
    public void Overrides_win_over_the_theme_and_bad_ones_are_ignored()
    {
        var settings = new ThemeSettings
        {
            Name = "Nord",
            Colors = new() { ["accent"] = "#FF8800", ["Bg"] = "nonsense", ["NoSuchSlot"] = "#000000" },
        };

        var palette = Themes.Resolve(settings);
        Assert.Equal(Theme.Rgb(255, 136, 0), palette[ThemeSlot.Accent]);
        Assert.Equal(Themes.Nord.Palette[ThemeSlot.Bg], palette[ThemeSlot.Bg]);
    }

    [Fact]
    public void Hex_colours_parse_with_or_without_the_hash_and_in_short_form()
    {
        Assert.True(HexColor.TryParse("#0a0B0c", out var c));
        Assert.Equal(Theme.Rgb(10, 11, 12), c);
        Assert.True(HexColor.TryParse("f80", out c));
        Assert.Equal(Theme.Rgb(255, 136, 0), c);
        Assert.False(HexColor.TryParse("#12345", out _));
        Assert.False(HexColor.TryParse(null, out _));
        Assert.Equal("#FF8800", HexColor.Format(Theme.Rgb(255, 136, 0)));
    }

    [Fact]
    public void Applying_settings_changes_what_the_views_read()
    {
        Theme.Apply(new ThemeSettings { Name = "Light" }.WithColor(ThemeSlot.Fg, "#112233"));
        Assert.Equal(Themes.Light.Palette[ThemeSlot.Bg], Theme.Bg);
        Assert.Equal(Theme.Rgb(0x11, 0x22, 0x33), Theme.Fg);
    }

    [Fact]
    public void An_override_is_replaced_not_duplicated_and_empty_removes_it()
    {
        var t = new ThemeSettings { Colors = new() { ["accent"] = "#111111" } }.WithColor(ThemeSlot.Accent, "#222222");
        Assert.Single(t.Colors);
        Assert.Equal("#222222", t.Override(ThemeSlot.Accent));
        Assert.Empty(t.WithColor(ThemeSlot.Accent, "").Colors);
    }

    [Fact]
    public void The_theme_survives_the_settings_file()
    {
        var store = new SettingsStore(_folder);
        store.Update(s => s with { Ui = s.Ui with { Theme = new ThemeSettings { Name = "Nord" }.WithColor(ThemeSlot.Good, "#00FF00") } });

        var loaded = new SettingsStore(_folder).Load().Ui.Theme;
        Assert.Equal("Nord", loaded.Name);
        Assert.Equal("#00FF00", loaded.Override(ThemeSlot.Good));
        Assert.Contains("\"Theme\"", File.ReadAllText(store.FilePath));
        Assert.Equal("Default", JsonSerializer.Deserialize<UserSettings>("{}", SettingsJson.Options)!.Ui.Theme.Name);
    }

    [Fact]
    public void A_colour_chosen_in_the_picker_is_saved_as_an_override_and_only_colour_rows_can_pick()
    {
        var store = new SettingsStore(_folder);
        var model = new SettingsModel(store, null);
        model.SetPage(model.Pages.Select(p => p.Title).ToList().IndexOf("Theme"));

        model.MoveTo(0);
        Assert.False(model.CanPickColor);
        Assert.Null(model.CurrentColor);

        model.MoveTo(model.Items.Select(i => i.Label).ToList().IndexOf("Accent"));
        Assert.True(model.CanPickColor);
        Assert.Equal(Themes.Default.Palette[ThemeSlot.Accent], model.CurrentColor);

        model.SetColor(Theme.Rgb(1, 2, 255));
        Assert.Equal("#0102FF", store.Current.Ui.Theme.Override(ThemeSlot.Accent));
        Assert.Equal(Theme.Rgb(1, 2, 255), model.CurrentColor);
    }

    [Fact]
    public void A_saved_theme_keeps_the_colour_changes_while_other_themes_are_tried()
    {
        var t = new ThemeSettings { Name = "Nord" }.WithColor(ThemeSlot.Accent, "#FF8800").SaveAs("  Mine ");

        Assert.Equal("Mine", t.Name);
        Assert.Empty(t.Colors);
        Assert.Equal("Nord", t.Saved["Mine"].Base);
        Assert.Equal(Theme.Rgb(255, 136, 0), Themes.Resolve(t)[ThemeSlot.Accent]);
        Assert.Equal(Themes.Nord.Palette[ThemeSlot.Bg], Themes.Resolve(t)[ThemeSlot.Bg]);

        var other = t with { Name = "Light" };
        Assert.Equal(Themes.Light.Palette[ThemeSlot.Accent], Themes.Resolve(other)[ThemeSlot.Accent]);
        Assert.Equal(Theme.Rgb(255, 136, 0), Themes.Resolve(other with { Name = "mine" })[ThemeSlot.Accent]);
        Assert.Equal("Mine", (other with { Name = "mine" }).DisplayName);
    }

    [Fact]
    public void Saving_again_updates_a_saved_theme_and_deleting_goes_back_to_its_base()
    {
        var t = new ThemeSettings { Name = "Nord" }.WithColor(ThemeSlot.Accent, "#111111").SaveAs("Mine")
            .WithColor(ThemeSlot.Good, "#222222").SaveAs("Mine");

        Assert.Single(t.Saved);
        Assert.Equal("#111111", t.Saved["Mine"].Colors["Accent"]);
        Assert.Equal("#222222", t.Saved["Mine"].Colors["Good"]);

        var deleted = t.DeleteSaved();
        Assert.Empty(deleted.Saved);
        Assert.Equal("Nord", deleted.Name);
        Assert.Same(deleted, deleted.DeleteSaved());
    }

    [Fact]
    public void A_saved_theme_cannot_hide_a_predefined_one_and_survives_the_settings_file()
    {
        var hidden = new ThemeSettings { Name = "Nord", Saved = new() { ["nord"] = new SavedTheme { Colors = new() { ["Bg"] = "#000000" } } } };
        Assert.Equal(Themes.Nord.Palette[ThemeSlot.Bg], Themes.Resolve(hidden)[ThemeSlot.Bg]);

        var store = new SettingsStore(_folder);
        store.Update(s => s with { Ui = s.Ui with { Theme = new ThemeSettings().WithColor(ThemeSlot.Bg, "#010203").SaveAs("Mine") } });
        var loaded = new SettingsStore(_folder).Load().Ui.Theme;
        Assert.Equal("Mine", loaded.Name);
        Assert.Equal(Theme.Rgb(1, 2, 3), Themes.Resolve(loaded)[ThemeSlot.Bg]);
    }

    [Fact]
    public void The_theme_page_saves_the_theme_under_a_name_and_lists_it_in_the_picker()
    {
        var store = new SettingsStore(_folder);
        var model = new SettingsModel(store, null);
        model.SetPage(model.Pages.Select(p => p.Title).ToList().IndexOf("Theme"));
        model.MoveTo(model.Items.Select(i => i.Label).ToList().IndexOf("Accent"));
        model.SetColor(Theme.Rgb(255, 136, 0));

        model.MoveTo(model.Items.Select(i => i.Label).ToList().IndexOf("Save theme as…"));
        model.BeginEdit();
        model.Type("Nord");
        model.CommitEdit();
        Assert.True(model.Editing);
        Assert.Empty(store.Current.Ui.Theme.Saved);
        model.CancelEdit();

        model.BeginEdit();
        model.Type("Mine");
        model.CommitEdit();
        Assert.Equal("Mine", store.Current.Ui.Theme.Name);
        Assert.Empty(store.Current.Ui.Theme.Colors);

        model.MoveTo(0);
        model.Change(1);
        Assert.Equal("Default", store.Current.Ui.Theme.Name);
        model.Change(-1);
        Assert.Equal("Mine", store.Current.Ui.Theme.Name);

        model.MoveTo(model.Items.Select(i => i.Label).ToList().IndexOf("Delete saved theme"));
        model.Activate();
        Assert.Empty(store.Current.Ui.Theme.Saved);
        Assert.Equal("Default", store.Current.Ui.Theme.Name);
    }

    [Fact]
    public void The_theme_page_picks_a_theme_edits_a_colour_and_resets()
    {
        var store = new SettingsStore(_folder);
        var model = new SettingsModel(store, null);
        model.SetPage(model.Pages.Select(p => p.Title).ToList().IndexOf("Theme"));

        model.MoveTo(0);
        model.Change(1);
        Assert.Equal(Themes.All[1].Name, store.Current.Ui.Theme.Name);

        model.MoveTo(model.Items.Select(i => i.Label).ToList().IndexOf("Accent"));
        model.BeginEdit();
        model.Type("zzz");
        model.CommitEdit();
        Assert.True(model.Editing);
        Assert.Empty(store.Current.Ui.Theme.Colors);
        model.CancelEdit();

        model.BeginEdit();
        for (var i = 0; i < 7; i++)
        {
            model.Backspace();
        }

        model.Type("#FF8800");
        model.CommitEdit();
        Assert.Equal("#FF8800", store.Current.Ui.Theme.Override(ThemeSlot.Accent));

        model.MoveTo(model.Items.Select(i => i.Label).ToList().IndexOf("Reset colour changes"));
        model.Activate();
        Assert.Empty(store.Current.Ui.Theme.Colors);
    }
}
