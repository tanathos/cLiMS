using Clims.Lms;

namespace Clims.Catalog.Tests;

/// <summary>Runs only when <c>CLIMS_CATALOG_DB</c> points at a saved index (the cache file the app made). Needs no server.</summary>
public sealed class SavedIndexFactAttribute : FactAttribute
{
    public SavedIndexFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLIMS_CATALOG_DB")))
        {
            Skip = "Set CLIMS_CATALOG_DB to a saved index (catalog-<uuid>.db) to time the local search on real data.";
        }
    }
}
