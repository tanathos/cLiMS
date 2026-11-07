using Clims.Catalog;
using Clims.Lms;

namespace Clims.Tui.Data;

/// <summary>An <see cref="ICatalogService"/> over the demo data: always "synced", nothing to sync or refresh.</summary>
public sealed class DemoCatalogService : ICatalogService
{
    public DemoCatalogService()
    {
        Current = DemoCatalog.Create();
        Status = new SyncStatus(SyncState.Demo, true, null, null, "Fake data; run with --lms <url> to use your server.");
    }

    public MusicCatalog? Current { get; }

    public SyncStatus Status { get; }

    public event Action<SyncStatus>? StatusChanged
    {
        add { }
        remove { }
    }

    public event Action<MusicCatalog?>? CatalogChanged
    {
        add { }
        remove { }
    }

    public event Action<SyncPrompt>? PromptRequested
    {
        add { }
        remove { }
    }

    public event Action<int>? AlbumRefreshed
    {
        add { }
        remove { }
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task CheckServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SyncAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void DeferSync()
    {
    }

    public void CancelSync()
    {
    }

    public Task<bool> RefreshAlbumAsync(int albumId, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task RunEventsAsync(ILmsEventStream events, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Dispose()
    {
    }
}
