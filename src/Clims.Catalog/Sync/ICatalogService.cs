using Clims.Lms;

namespace Clims.Catalog;

/// <summary>
/// Owns the local index of one server: loads it, keeps its sync status honest, syncs when (and only when) asked, and
/// refreshes single albums on demand. Independent of any UI; events may fire on any thread.
/// </summary>
public interface ICatalogService : IDisposable
{
    /// <summary>The catalogue to browse, or null when there is none yet (not synced and no sync data).</summary>
    Catalog? Current { get; }

    /// <summary>Current sync status.</summary>
    SyncStatus Status { get; }

    /// <summary>Raised when <see cref="Status"/> changes.</summary>
    event Action<SyncStatus>? StatusChanged;

    /// <summary>Raised when <see cref="Current"/> is replaced (loaded from disk, partial first build, finished sync).</summary>
    event Action<Catalog?>? CatalogChanged;

    /// <summary>Raised when the user should be asked whether to sync. Answer with <see cref="SyncAsync"/> or <see cref="DeferSync"/>.</summary>
    event Action<SyncPrompt>? PromptRequested;

    /// <summary>Raised after an on-view refresh changed the tracks of an album.</summary>
    event Action<int>? AlbumRefreshed;

    /// <summary>Loads the saved index and compares it with the server. Never starts a sync.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Compares the index with the server again (after a finished scan, or on reconnect).</summary>
    Task CheckServerAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs a full sync. Call only after the user agreed. Completes when the sync ends (or fails or is cancelled).</summary>
    Task SyncAsync(CancellationToken cancellationToken = default);

    /// <summary>The user chose "Later": do not ask again until the next launch (or until the server changes again).</summary>
    void DeferSync();

    /// <summary>Cancels a running sync; the previous index keeps serving.</summary>
    void CancelSync();

    /// <summary>Re-reads one album's tracks from the server and updates the index. True when something changed.</summary>
    Task<bool> RefreshAlbumAsync(int albumId, CancellationToken cancellationToken = default);

    /// <summary>Follows server events: a finished scan or library change re-checks the server; a dropped connection is noticed.</summary>
    Task RunEventsAsync(ILmsEventStream events, CancellationToken cancellationToken = default);
}
