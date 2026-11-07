using Clims.Lms;

namespace Clims.Catalog.Tests;

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
