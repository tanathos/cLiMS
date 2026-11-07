using Clims.Lms;

namespace Clims.Catalog.Tests;

public class SavedIndexSearchTests
{
    [SavedIndexFact]
    public void Local_search_on_the_real_index_is_well_under_fifty_milliseconds()
    {
        // Work on a copy: opening the file must not touch the app's cache.
        var copy = Path.Combine(Path.GetTempPath(), $"clims-search-{Guid.NewGuid():N}.db");
        File.Copy(Environment.GetEnvironmentVariable("CLIMS_CATALOG_DB")!, copy);
        try
        {
            var catalog = new SqliteCatalogStore(copy).Load();
            Assert.NotNull(catalog);
            _ = catalog.Search("warm up"); // builds the track index once
            var report = new List<string>();
            foreach (var q in new[] { "e", "a", "the", "i", "iron", "iron m", "maid", "black sab", "zzzz" })
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                RankedSearchResult r = catalog.Search(q);
                for (var i = 0; i < 9; i++)
                {
                    r = catalog.Search(q);
                }

                var ms = sw.Elapsed.TotalMilliseconds / 10;
                report.Add($"'{q}': {ms:F1} ms ({r.Artists.Total} artists, {r.Albums.Total} albums, {r.Tracks.Total} tracks, {r.Genres.Total} genres)");
                Assert.True(ms < 50, string.Join("; ", report));
            }

            Console.WriteLine(string.Join(Environment.NewLine, report));
            File.WriteAllLines(Path.Combine(Path.GetTempPath(), "clims-search-timings.txt"), report);
        }
        finally
        {
            File.Delete(copy);
        }
    }
}
