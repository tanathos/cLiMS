using Clims.Catalog;
using Terminal.Gui.ViewBase;

namespace Clims.Tui.Ui;

/// <summary>Asks the server for the ids of the newest albums, newest first (LMS <c>albums … sort:new</c>).</summary>
public delegate Task<IReadOnlyList<int>> RecentAlbumsSource(int count, CancellationToken cancellationToken);

/// <summary>
/// "Recently added" tab: the Library's album list, full width, holding only the albums the server added last. The order
/// comes from the server (it knows when files were added); everything shown comes from the local index.
/// </summary>
public sealed class RecentTab : View
{
    /// <summary>How many albums the tab lists.</summary>
    public const int Count = 100;

    public const string Heading = "Recently added";

    private readonly RecentAlbumsSource _source;
    private CancellationTokenSource? _loading;
    private IReadOnlyList<int> _ids = Array.Empty<int>();
    private MusicCatalog? _catalog;
    private string _emptyMessage = "Library not synced yet. Press F5 to sync.";
    private string? _failure;
    private bool _loaded;

    public RecentTab(RecentAlbumsSource source)
    {
        _source = source;
        CanFocus = true;
        Albums = new AlbumsView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), ShowArtist = true, Sortable = false };
        Add(Albums);
        Rebuild();
    }

    public AlbumsView Albums { get; }

    /// <summary>The catalogue changed (loaded, synced): list the albums again, and fetch the order again.</summary>
    public void SetCatalog(MusicCatalog? catalog, string emptyMessage)
    {
        _catalog = catalog;
        _emptyMessage = emptyMessage;
        Rebuild();
    }

    /// <summary>The tab was opened: ask the server what is newest now (new music may have been added since last time).</summary>
    public void Refresh()
    {
        _loading?.Cancel();
        _loading?.Dispose();
        var cts = _loading = new CancellationTokenSource();
        _ = LoadAsync(cts.Token);
    }

    /// <summary>Stops a request still in flight (the tab was left, or the window closes).</summary>
    public void Cancel()
    {
        _loading?.Cancel();
    }

    /// <summary>The newest albums that the index knows, in the order of <paramref name="ids"/>; unknown ids (added after the last sync) are skipped.</summary>
    public static IReadOnlyList<CatalogAlbum> Resolve(IEnumerable<int> ids, MusicCatalog catalog)
    {
        var list = new List<CatalogAlbum>();
        var seen = new HashSet<int>();
        foreach (var id in ids)
        {
            if (seen.Add(id) && catalog.Album(id) is { } album)
            {
                list.Add(album);
            }
        }

        return list;
    }

    private async Task LoadAsync(CancellationToken token)
    {
        try
        {
            var ids = await _source(Count, token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                return;
            }

            Invoke(() =>
            {
                _ids = ids;
                _failure = null;
                _loaded = true;
                Rebuild();
            });
        }
        catch (OperationCanceledException)
        {
            // Left the tab, or a newer request replaced this one.
        }
        catch (Exception ex) when (ex is Clims.Lms.LmsException or HttpRequestException or IOException)
        {
            Invoke(() =>
            {
                _failure = "The server did not answer. Press 4 to try again.";
                Rebuild();
            });
        }
    }

    private void Invoke(Action action)
    {
        if (App is { } app)
        {
            app.Invoke(action);
        }
        else
        {
            action();
        }
    }

    private void Rebuild()
    {
        if (_catalog is null || _catalog.Albums.Count == 0)
        {
            Albums.SetMessage(_emptyMessage);
            return;
        }

        if (_failure is not null && _ids.Count == 0)
        {
            Albums.SetMessage(_failure);
            return;
        }

        if (!_loaded)
        {
            Albums.SetMessage("Asking the server for the newest albums…");
            return;
        }

        var albums = Resolve(_ids, _catalog);
        if (albums.Count == 0)
        {
            Albums.SetMessage("Nothing new: the server lists no albums that this library knows. Press F5 to resync.");
            return;
        }

        Albums.SetAlbums($"{Heading} · {albums.Count} albums, newest first", albums, _catalog);
    }
}
