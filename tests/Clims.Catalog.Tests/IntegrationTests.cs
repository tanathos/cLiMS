using Clims.Lms;

namespace Clims.Catalog.Tests;

/// <summary>Runs only when <c>CLIMS_LMS_URL</c> points at a real server. Read-only: the sync only issues queries.</summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLIMS_LMS_URL")))
        {
            Skip = "Set CLIMS_LMS_URL (for example http://192.168.1.20:9000/) to run the read-only sync against a real server.";
        }
    }
}

public class SyncIntegrationTests
{
    private static LmsClient Client()
    {
        var url = Environment.GetEnvironmentVariable("CLIMS_LMS_URL")!;
        return new LmsClient(new LmsClientOptions { BaseUri = new Uri(url.EndsWith('/') ? url : url + "/"), Timeout = TimeSpan.FromSeconds(60) });
    }

    [IntegrationFact]
    public async Task A_full_sync_against_the_real_server_matches_its_counts_and_survives_a_disk_roundtrip()
    {
        using var client = Client();
        using var dir = new TempFolder();
        var store = new SqliteCatalogStore(dir.File("real.db"));
        using var service = new CatalogService(client, new CatalogServiceOptions { StoreFactory = _ => store });
        SyncStatus? last = null;
        service.StatusChanged += s => last = s;
        await service.StartAsync();
        Assert.Equal(SyncState.NotSynced, service.Status.State);

        await service.SyncAsync();

        Assert.True(service.Status.State == SyncState.Synced, $"sync ended as {service.Status.State}: {service.Status.Detail} (last status {last})");
        var catalog = service.Current!;
        var server = await client.Server.GetStatusAsync();
        Assert.Equal(server.ArtistCount, catalog.Artists.Count);
        Assert.Equal(server.AlbumCount, catalog.Albums.Count);
        Assert.Equal(server.GenreCount, catalog.Genres.Count);
        Assert.InRange(catalog.TrackCount, server.TrackCount - 5, server.TrackCount); // tracks without an album are skipped
        Assert.All(catalog.Albums.Take(200), a => Assert.False(string.IsNullOrEmpty(a.Title)));

        var reloaded = store.Load()!;
        Assert.Equal(catalog.Albums.Count, reloaded.Albums.Count);
        Assert.Equal(catalog.TrackCount, reloaded.TrackCount);

        // A second launch finds the saved index up to date and asks nothing.
        using var second = new CatalogService(client, new CatalogServiceOptions { StoreFactory = _ => store, KnownServerUuid = server.Uuid });
        var prompts = 0;
        second.PromptRequested += _ => prompts++;
        await second.StartAsync();
        Assert.Equal(SyncState.Synced, second.Status.State);
        Assert.Equal(0, prompts);
    }

    [IntegrationFact]
    public async Task Refreshing_a_real_album_returns_its_tracks_with_file_urls()
    {
        using var client = Client();
        using var dir = new TempFolder();
        using var service = new CatalogService(client, new CatalogServiceOptions { StoreFactory = _ => new SqliteCatalogStore(dir.File("r.db")) });
        await service.StartAsync();
        await service.SyncAsync();
        var catalog = service.Current!;
        var album = catalog.Albums.First(a => catalog.TracksOf(a.Id).Count > 0);

        await service.RefreshAlbumAsync(album.Id);

        Assert.All(catalog.TracksOf(album.Id), t => Assert.False(string.IsNullOrEmpty(t.Url)));
    }
}

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
