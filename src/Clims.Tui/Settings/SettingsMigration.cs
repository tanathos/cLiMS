using System.Text.Json;
using System.Text.Json.Nodes;
using Clims.Tui.Ui;

namespace Clims.Tui.Settings;

/// <summary>Turns a version 1 settings file (flat keys: LmsUrl, Waveform, CoverCache, Lyrics...) into <see cref="UserSettings"/>.</summary>
public static class SettingsMigration
{
    /// <summary>True when the file has no Version, or one below 2.</summary>
    /// <exception cref="JsonException">The text is not a JSON object.</exception>
    public static bool IsOldVersion(string json) => Version(Parse(json)) < UserSettings.CurrentVersion;

    /// <summary>The version 2 equivalent of a version 1 file. Keys that did not exist in version 1 take their defaults.</summary>
    /// <exception cref="JsonException">The text is not a JSON object.</exception>
    public static UserSettings FromV1(string json)
    {
        var root = Parse(json);
        var url = Text(root, "LmsUrl");
        var uuid = Text(root, "ServerUuid");

        var servers = new List<ServerSettings>();
        if (url is not null)
        {
            servers.Add(new ServerSettings
            {
                Name = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url,
                Url = url,
                Uuid = uuid,
                Username = Text(root, "Username"),
                ProtectedPassword = Text(root, "ProtectedPassword"),
                PlayerId = Text(root, "PlayerId"),
                Lyrics = Lyrics(Node(root, "Lyrics")),
            });
        }

        return new UserSettings
        {
            Servers = servers,
            Startup = new StartupSettings { Server = uuid ?? url },
            Cache = new CacheSettings { Covers = Covers(Node(root, "CoverCache")) },
            Ui = new UiSettings
            {
                Waveform = new WaveformSettings
                {
                    Style = Enum.TryParse<WaveformStyle>(Text(root, "Waveform"), ignoreCase: true, out var style) && Enum.IsDefined(style) ? style : WaveformStyle.Blocks,
                },
            },
        };
    }

    private static CoverCacheSettings Covers(JsonObject? node)
    {
        try
        {
            return node?.Deserialize<CoverCacheSettings>(SettingsJson.Options) ?? new CoverCacheSettings();
        }
        catch (JsonException)
        {
            return new CoverCacheSettings();
        }
    }

    private static ServerLyricsSettings Lyrics(JsonObject? node)
    {
        var defaults = new ServerLyricsSettings();
        if (node is null)
        {
            return defaults;
        }

        var map = new Dictionary<string, string>();
        if (Node(node, "MusicPathMap") is { } pathMap)
        {
            foreach (var (key, value) in pathMap)
            {
                if (value is JsonValue v && v.TryGetValue<string>(out var target))
                {
                    map[key] = target;
                }
            }
        }

        var providers = (Find(node, "Providers") as JsonArray)
            ?.Select(p => p is JsonValue v && v.TryGetValue<string>(out var name) ? name : null).OfType<string>().ToList();

        var files = new LyricsFilesSettings
        {
            PathMap = map,
            ShareUser = Text(node, "MusicShareUser"),
            SharePassword = Text(node, "MusicSharePassword"),
            ProtectedSharePassword = Text(node, "MusicShareProtectedPassword"),
        };
        if (files.SharePassword is { Length: > 0 } plain && SecretStore.Protect(plain) is { } protectedValue)
        {
            files = files with { SharePassword = null, ProtectedSharePassword = protectedValue };
        }

        return new ServerLyricsSettings
        {
            Enabled = Flag(node, "Enabled") ?? defaults.Enabled,
            OnlineLookup = Flag(node, "OnlineLookup") ?? defaults.OnlineLookup,
            Providers = providers ?? defaults.Providers,
            MissRetryDays = Number(node, "MissRetryDays") ?? defaults.MissRetryDays,
            Files = files,
        };
    }

    private static JsonObject Parse(string json) =>
        JsonNode.Parse(json) as JsonObject ?? throw new JsonException("The settings file is not a JSON object.");

    private static int Version(JsonObject root) => Number(root, "Version") ?? 1;

    private static JsonNode? Find(JsonObject node, string name) =>
        node.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static JsonObject? Node(JsonObject node, string name) => Find(node, name) as JsonObject;

    private static string? Text(JsonObject node, string name) =>
        Find(node, name) is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    private static bool? Flag(JsonObject node, string name) =>
        Find(node, name) is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    private static int? Number(JsonObject node, string name) =>
        Find(node, name) is JsonValue v && v.TryGetValue<int>(out var n) ? n : null;
}
