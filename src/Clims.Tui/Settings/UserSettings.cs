using System.Text.Json.Serialization;

namespace Clims.Tui.Settings;

/// <summary>The settings file, version 2 (settings.json in the per-user app data folder).</summary>
public sealed record UserSettings
{
    public const int CurrentVersion = 2;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Every server cLiMS has been connected to.</summary>
    public List<ServerSettings> Servers { get; init; } = [];

    public StartupSettings Startup { get; init; } = new();

    public CacheSettings Cache { get; init; } = new();

    [JsonPropertyName("UI")]
    public UiSettings Ui { get; init; } = new();

    /// <summary>The server named by <see cref="StartupSettings.Server"/>, else the first one, else null.</summary>
    [JsonIgnore]
    public ServerSettings? ActiveServer => Servers.FirstOrDefault(s => s.Matches(Startup.Server)) ?? Servers.FirstOrDefault();

    /// <summary>The entry for a server: by UUID when given, else by address.</summary>
    public ServerSettings? Find(string url, string? uuid = null) =>
        (uuid is null ? null : Servers.FirstOrDefault(s => s.Matches(uuid))) ?? Servers.FirstOrDefault(s => s.Matches(url));

    /// <summary>
    /// Adds the server (named after its host) or changes its entry, keeps the entry's address and UUID up to date, and makes it the
    /// server to open next time. The list order is kept.
    /// </summary>
    /// <param name="url">The server's address.</param>
    /// <param name="uuid">Its UUID when known (it also finds the entry again if the address changed).</param>
    /// <param name="change">What else to change in the entry.</param>
    public UserSettings WithServer(string url, string? uuid = null, Func<ServerSettings, ServerSettings>? change = null)
    {
        var existing = Find(url, uuid);
        var entry = existing ?? new ServerSettings { Name = Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.Host : url };
        entry = entry with { Url = url, Uuid = uuid ?? entry.Uuid };
        if (change is not null)
        {
            entry = change(entry);
        }

        var servers = existing is null ? [.. Servers, entry] : Servers.Select(s => ReferenceEquals(s, existing) ? entry : s).ToList();
        return this with { Servers = servers, Startup = Startup with { Server = entry.Uuid ?? entry.Url } };
    }
}
