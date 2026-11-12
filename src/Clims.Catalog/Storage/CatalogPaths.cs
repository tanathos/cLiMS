using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Clims.Catalog;

/// <summary>Where the per-server cache files live: the per-user app data folder, safe to delete at any time.</summary>
public static class CatalogPaths
{
    /// <summary>The folder for cLiMS data (created on demand). The <c>CLIMS_DATA</c> environment variable names another one (for tests and portable use).</summary>
    public static string DataFolder
    {
        get
        {
            if (Environment.GetEnvironmentVariable("CLIMS_DATA") is { Length: > 0 } custom)
            {
                return custom;
            }

            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root))
            {
                root = Path.GetTempPath();
            }

            return Path.Combine(root, "cLiMS");
        }
    }

    /// <summary>The lyrics store: one file for every server (lyrics belong to the song). Unlike the caches it holds things that cost a search to get back.</summary>
    public static string LyricsFile(string? folder = null) => Path.Combine(folder ?? DataFolder, "lyrics.db");

    /// <summary>The cache file for a server, keyed by its UUID so that changing the address does not lose the index.</summary>
    public static string For(string serverUuid, string? folder = null) => Named("catalog", serverUuid, folder);

    /// <summary>The cover cache file for a server (apart from the index, so each can be deleted on its own).</summary>
    public static string CoversFor(string serverUuid, string? folder = null) => Named("covers", serverUuid, folder);

    private static string Named(string prefix, string serverUuid, string? folder)
    {
        var safe = new string(serverUuid.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return Path.Combine(folder ?? DataFolder, $"{prefix}-{(safe.Length == 0 ? "unknown" : safe)}.db");
    }
}
