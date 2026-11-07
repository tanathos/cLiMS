using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Clims.Catalog;

/// <summary>Where the per-server cache files live: the per-user app data folder, safe to delete at any time.</summary>
public static class CatalogPaths
{
    /// <summary>The folder for cLiMS data (created on demand).</summary>
    public static string DataFolder
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root))
            {
                root = Path.GetTempPath();
            }

            return Path.Combine(root, "cLiMS");
        }
    }

    /// <summary>The cache file for a server, keyed by its UUID so that changing the address does not lose the index.</summary>
    public static string For(string serverUuid, string? folder = null)
    {
        var safe = new string(serverUuid.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return Path.Combine(folder ?? DataFolder, $"catalog-{(safe.Length == 0 ? "unknown" : safe)}.db");
    }
}
