using System.Text.Json;
using System.Text.Json.Serialization;
using Clims.Catalog;
using Clims.Tui.Ui;

namespace Clims.Tui;

/// <summary>Settings remembered between runs, in the per-user app data folder (safe to delete).</summary>
/// <param name="LmsUrl">The server address last used.</param>
/// <param name="ServerUuid">UUID of that server, so its saved index can be used while it is unreachable.</param>
/// <param name="PlayerId">The player to control (its id, usually the MAC address). Null: the first connected hardware player.</param>
/// <param name="Username">Login for a password-protected server.</param>
/// <param name="ProtectedPassword">The password, protected by <see cref="SecretStore"/> (never plain text).</param>
public sealed record AppSettings(string? LmsUrl = null, string? ServerUuid = null, string? PlayerId = null, string? Username = null, string? ProtectedPassword = null)
{
    /// <summary>The cover cache options (the "CoverCache" node). Written back on save, so the defaults are visible in the file.</summary>
    public CoverCacheSettings CoverCache { get; init; } = new();

    /// <summary>The lyrics options (the "Lyrics" node), written back on save like <see cref="CoverCache"/>.</summary>
    public LyricsSettings Lyrics { get; init; } = new();

    /// <summary>The footer's waveform style at start-up: "Blocks" (the default), "Braille" or "Sixel". Click the waveform to switch for the session.</summary>
    public string Waveform { get; init; } = nameof(WaveformStyle.Blocks);

    /// <summary><see cref="Waveform"/> as a style; anything unrecognised is the default.</summary>
    [JsonIgnore]
    public WaveformStyle WaveformStyle => Enum.TryParse<WaveformStyle>(Waveform, ignoreCase: true, out var style) && Enum.IsDefined(style) ? style : WaveformStyle.Blocks;

    private static string FilePath => Path.Combine(CatalogPaths.DataFolder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(CatalogPaths.DataFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; failing to save them must not stop the app.
        }
    }

    /// <summary>Accepts <c>host</c>, <c>host:9000</c> or a full URL, and returns an http URI ending in a slash; null if unusable.</summary>
    /// <param name="text">What the user typed.</param>
    /// <param name="defaultPort">Port to add when the text is a bare host without scheme or port (null: leave it to http's 80).</param>
    public static Uri? NormalizeUrl(string? text, int? defaultPort = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var t = text.Trim();
        if (!t.Contains("://", StringComparison.Ordinal))
        {
            t = "http://" + t;
        }

        if (!t.EndsWith('/'))
        {
            t += "/";
        }

        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        if (defaultPort is { } port && !text.Contains("://", StringComparison.Ordinal) && uri.IsDefaultPort)
        {
            return new UriBuilder(uri) { Port = port }.Uri;
        }

        return uri;
    }
}
