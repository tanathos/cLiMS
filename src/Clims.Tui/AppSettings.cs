using System.Text.Json;
using Clims.Catalog;

namespace Clims.Tui;

/// <summary>Settings remembered between runs, in the per-user app data folder (safe to delete).</summary>
/// <param name="LmsUrl">The server address last used.</param>
/// <param name="ServerUuid">UUID of that server, so its saved index can be used while it is unreachable.</param>
/// <param name="PlayerId">The player to control (its id, usually the MAC address). Null: the first connected hardware player.</param>
public sealed record AppSettings(string? LmsUrl = null, string? ServerUuid = null, string? PlayerId = null)
{
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
    public static Uri? NormalizeUrl(string? text)
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

        return Uri.TryCreate(t, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;
    }
}
