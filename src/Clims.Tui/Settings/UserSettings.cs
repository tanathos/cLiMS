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
}
