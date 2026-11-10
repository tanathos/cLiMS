using Clims.Tui.Ui;

namespace Clims.Tui.Settings;

/// <summary>The tabs of the settings page and their rows, built from the current settings (a server added or removed shows at once).</summary>
public static class SettingsPages
{
    private static readonly CoverMode[] Styles = [CoverMode.Sixel, CoverMode.HalfBlock, CoverMode.Quadrant, CoverMode.Braille];
    private static readonly CoverMode[] Fallbacks = [CoverMode.HalfBlock, CoverMode.Quadrant, CoverMode.Braille];
    private static readonly CoverSize[] Sizes = [CoverSize.Small, CoverSize.Medium, CoverSize.Large];
    private static readonly WaveformStyle[] Waveforms = [WaveformStyle.Blocks, WaveformStyle.Braille, WaveformStyle.Sixel];
    private static readonly StartupView[] Views = [StartupView.Library, StartupView.Recent, StartupView.Queue, StartupView.Genres, StartupView.Search];

    /// <param name="settings">The settings as they are now.</param>
    /// <param name="connectedUrl">The address of the server in use, or null in demo mode.</param>
    public static IReadOnlyList<SettingsPage> Build(UserSettings settings, string? connectedUrl) =>
    [
        new("Servers", ServerRows(settings, connectedUrl)),
        new("UI", UiRows()),
        new("Lyrics", LyricsRows(settings)),
        new("Cache", CacheRows()),
        new("Startup", StartupRows()),
    ];

    private static List<SettingItem> ServerRows(UserSettings settings, string? connectedUrl)
    {
        var rows = new List<SettingItem>();
        foreach (var server in settings.Servers)
        {
            var key = server.Uuid ?? server.Url ?? "";
            var opensAtStart = ReferenceEquals(settings.ActiveServer, server);
            rows.Add(new SettingItem
            {
                Label = server.Name ?? server.Url ?? "(unnamed)",
                Kind = SettingKind.Server,
                ServerKey = key,
                Connected = connectedUrl is not null && server.Url is not null && string.Equals(connectedUrl, server.Url, StringComparison.OrdinalIgnoreCase),
                Value = _ => server.Url + (opensAtStart ? "  · opens at start" : ""),
                EditText = _ => server.Name ?? "",
                Edit = (s, name) => s with { Servers = s.Servers.Select(x => x.Matches(key) ? x with { Name = name.Trim() } : x).ToList() },
            });
        }

        if (rows.Count == 0)
        {
            rows.Add(SettingItems.Info("No server yet", "add one below"));
        }

        rows.Add(new SettingItem { Label = "+ Add a server…", Kind = SettingKind.Action, Value = _ => "opens the connect screen" });
        return rows;
    }

    private static List<SettingItem> UiRows() =>
    [
        SettingItems.Choice("Library covers", s => s.Ui.Covers.Library.Style, (s, v) => Covers(s, c => c with { Library = c.Library with { Style = v } }), Styles, "lists: library, recent, search, genres"),
        SettingItems.Choice("Library cover size", s => s.Ui.Covers.Library.Size ?? CoverSize.Medium, (s, v) => Covers(s, c => c with { Library = c.Library with { Size = v } }), Sizes, "16x8, 24x12 or 32x16 cells"),
        SettingItems.Choice("Footer cover", s => s.Ui.Covers.Footer.Style, (s, v) => Covers(s, c => c with { Footer = c.Footer with { Style = v } }), Styles),
        SettingItems.Choice("Full-screen cover", s => s.Ui.Covers.FullScreen.Style, (s, v) => Covers(s, c => c with { FullScreen = c.FullScreen with { Style = v } }), Styles),
        SettingItems.Choice("Where Sixel is not possible", s => s.Ui.Covers.SixelFallback, (s, v) => Covers(s, c => c with { SixelFallback = v }), Fallbacks, "terminals without Sixel, covers partly out of view"),
        SettingItems.Choice("Waveform style", s => s.Ui.Waveform.Style, (s, v) => s with { Ui = s.Ui with { Waveform = s.Ui.Waveform with { Style = v } } }, Waveforms, "clicking the waveform changes it too"),
        SettingItems.Toggle("Waveform", s => s.Ui.Waveform.Enabled, (s, v) => s with { Ui = s.Ui with { Waveform = s.Ui.Waveform with { Enabled = v } } }, "off: none shown, none downloaded", restart: true),
    ];

    private static List<SettingItem> LyricsRows(UserSettings settings)
    {
        if (settings.ActiveServer is not { Url: not null })
        {
            return [SettingItems.Info("No server yet", "lyrics settings belong to a server")];
        }

        var files = settings.ActiveServer.Lyrics.Files;
        return
        [
            SettingItems.Info("Server", settings.ActiveServer.Name ?? settings.ActiveServer.Url!, "the lyrics settings of the server that opens at start"),
            SettingItems.Toggle("Lyrics", s => Lyrics(s).Enabled, (s, v) => Lyr(s, l => l with { Enabled = v }), "off: none shown, stored or searched", restart: true),
            SettingItems.Toggle("Search online", s => Lyrics(s).OnlineLookup, (s, v) => Lyr(s, l => l with { OnlineLookup = v }), "sends artist, album, title and length to the providers", restart: true),
            Provider("lrclib", "synced lyrics, the best"),
            Provider("darklyrics", "metal, plain text"),
            Provider("lyricsovh", "plain text"),
            SettingItems.Number("Retry a miss after (days)", s => Lyrics(s).MissRetryDays, (s, v) => Lyr(s, l => l with { MissRetryDays = v }), 0, 365, 1, zeroText: "every time", restart: true),
            SettingItems.Info("Lyrics files", files.PathMap.Count == 0 ? "none" : $"{files.PathMap.Count} folder(s)", "the folder map and share login are edited in settings.json"),
        ];
    }

    private static List<SettingItem> CacheRows() =>
    [
        SettingItems.Toggle("Cover cache", s => s.Cache.Covers.Enabled, (s, v) => Cache(s, c => c with { Enabled = v }), "off: covers are always fetched from the server", restart: true),
        SettingItems.Number("Covers: days kept", s => s.Cache.Covers.Normal.TtlDays, (s, v) => Cache(s, c => c with { Normal = c.Normal with { TtlDays = v } }), 0, 3650, 5, zeroText: "not cached", hint: "lists and the footer (300 px)", restart: true),
        SettingItems.Number("Covers: most kept", s => s.Cache.Covers.Normal.MaxEntries, (s, v) => Cache(s, c => c with { Normal = c.Normal with { MaxEntries = v } }), 0, 100000, 250, zeroText: "no limit", restart: true),
        SettingItems.Number("Big covers: days kept", s => s.Cache.Covers.Big.TtlDays, (s, v) => Cache(s, c => c with { Big = c.Big with { TtlDays = v } }), 0, 3650, 5, zeroText: "not cached", hint: "the full-screen cover (900 px)", restart: true),
        SettingItems.Number("Big covers: most kept", s => s.Cache.Covers.Big.MaxEntries, (s, v) => Cache(s, c => c with { Big = c.Big with { MaxEntries = v } }), 0, 100000, 10, zeroText: "no limit", restart: true),
    ];

    private static List<SettingItem> StartupRows() =>
    [
        SettingItems.Choice("First tab", s => s.Startup.View, (s, v) => s with { Startup = s.Startup with { View = v } }, Views, "the tab shown when cLiMS starts", restart: true),
        SettingItems.Toggle("Connect at once", s => s.Startup.AutoConnect, (s, v) => s with { Startup = s.Startup with { AutoConnect = v } }, "off: the connect screen opens at every start", restart: true),
    ];

    private static SettingItem Provider(string name, string hint) => SettingItems.Toggle(
        "Provider " + name,
        s => Lyrics(s).Providers.Contains(name),
        (s, on) => Lyr(s, l => l with
        {
            // Kept in the order of KnownProviders, so the best source is asked first.
            Providers = ServerLyricsSettings.KnownProviders.Where(p => p == name ? on : l.Providers.Contains(p)).ToList(),
        }),
        hint,
        restart: true);

    private static ServerLyricsSettings Lyrics(UserSettings s) => s.ActiveServer?.Lyrics ?? new ServerLyricsSettings();

    private static UserSettings Lyr(UserSettings s, Func<ServerLyricsSettings, ServerLyricsSettings> change) =>
        s.ActiveServer is { Url: { } url } server ? s.WithServer(url, server.Uuid, x => x with { Lyrics = change(x.Lyrics) }) : s;

    private static UserSettings Covers(UserSettings s, Func<CoverStylesSettings, CoverStylesSettings> change) =>
        s with { Ui = s.Ui with { Covers = change(s.Ui.Covers) } };

    private static UserSettings Cache(UserSettings s, Func<CoverCacheSettings, CoverCacheSettings> change) =>
        s with { Cache = s.Cache with { Covers = change(s.Cache.Covers) } };
}
