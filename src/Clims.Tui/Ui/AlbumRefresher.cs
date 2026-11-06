using Clims.Catalog;

namespace Clims.Tui.Ui;

/// <summary>
/// On-view refresh: when an album shows up on screen, its tracks are re-read from the server and the index is updated.
/// That is how edits made by targeted rescans (which no cheap signal reveals) show up exactly where the user looks.
/// Two requests at a time, once per album per catalogue, cancelled if the album scrolls away before it starts.
/// </summary>
public sealed class AlbumRefresher : IDisposable
{
    private readonly ICatalogService _service;
    private readonly SemaphoreSlim _slots;
    private readonly object _gate = new();
    private readonly HashSet<int> _done = new();
    private readonly Dictionary<int, CancellationTokenSource> _running = new();
    private HashSet<int> _visible = new();

    public AlbumRefresher(ICatalogService service, int concurrency = 2)
    {
        _service = service;
        _slots = new SemaphoreSlim(Math.Max(1, concurrency));
    }

    /// <summary>Tells the refresher which albums are on screen now.</summary>
    public void SetVisible(IReadOnlyList<CatalogAlbum> albums)
    {
        var start = new List<int>();
        lock (_gate)
        {
            _visible = albums.Select(a => a.Id).ToHashSet();
            foreach (var (id, cts) in _running.Where(r => !_visible.Contains(r.Key)).ToList())
            {
                cts.Cancel();
            }

            foreach (var id in _visible)
            {
                if (!_done.Contains(id) && !_running.ContainsKey(id))
                {
                    _running[id] = new CancellationTokenSource();
                    start.Add(id);
                }
            }
        }

        foreach (var id in start)
        {
            _ = RefreshAsync(id);
        }
    }

    /// <summary>A new catalogue replaced the old one: albums may be refreshed again.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _done.Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var cts in _running.Values)
            {
                cts.Cancel();
            }
        }
    }

    private async Task RefreshAsync(int albumId)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            cts = _running[albumId];
        }

        var acquired = false;
        var completed = false;
        try
        {
            await _slots.WaitAsync(cts.Token).ConfigureAwait(false);
            acquired = true;

            // A slot can free up just before this album's cancellation arrives: check again that it is still on screen.
            lock (_gate)
            {
                if (!_visible.Contains(albumId))
                {
                    return;
                }
            }

            await _service.RefreshAlbumAsync(albumId, cts.Token).ConfigureAwait(false);
            completed = true;
        }
        catch (OperationCanceledException)
        {
            // Scrolled away before or during the request; it will be retried the next time the album is shown.
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex); // a refresh must never take the UI down
            completed = true;
        }
        finally
        {
            if (acquired)
            {
                _slots.Release();
            }

            lock (_gate)
            {
                _running.Remove(albumId);
                if (completed)
                {
                    _done.Add(albumId);
                }
            }

            cts.Dispose();
        }
    }
}
