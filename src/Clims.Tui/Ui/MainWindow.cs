using Clims.Catalog;
using Clims.Tui.Data;
using Clims.Waveform;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Clims.Tui.Ui;

public sealed class MainWindow : Window
{
    private const int MinWidth = 100;
    private const int MinHeight = 30;
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(5);

    private readonly IApplication _app;
    private readonly ICatalogService _catalog;
    private readonly ISearchService _searchService;
    private readonly IPlayerController _player;
    private readonly AlbumRefresher _refresher;
    private readonly HeaderBar _header;
    private readonly TabStrip _tabs;
    private readonly View[] _pages;
    private readonly LibraryTab _library;
    private readonly SearchTab _search;
    private readonly FooterView _footer;
    private readonly QueueTab _queue;
    private readonly WaveformService? _waves;
    private string _waveSignature = "";
    private bool _helpVisible;
    private readonly HelpOverlay _help = new();
    private int _currentTab = -1;
    private int _tabBeforeSearch = 1;
    private DateTime _lastTick = DateTime.UtcNow;
    private DateTime _noticeUntil = DateTime.MinValue;
    private int _coversDirty;
    private bool _sixelSeen;

    public MainWindow(
        IApplication app,
        ICatalogService catalog,
        ISearchService search,
        IPlayerController player,
        SavedQueueStore queues,
        WaveformService? waves = null)
    {
        _waves = waves;
        _app = app;
        _catalog = catalog;
        _searchService = search;
        _player = player;
        _refresher = new AlbumRefresher(catalog);

        Title = "";
        BorderStyle = LineStyle.None;
        SetScheme(Theme.Dark);

        _header = new HeaderBar(player, () => catalog.Status) { X = 0, Y = 0 };
        _tabs = new TabStrip("Search", "Library", "Queue") { X = 0, Y = HeaderBar.Rows, Width = Dim.Fill(), Height = 1 };

        _search = new SearchTab();
        _library = new LibraryTab();
        _queue = new QueueTab(player, queues);
        _pages = new View[] { _search, _library, _queue };
        foreach (var page in _pages)
        {
            page.X = 0;
            page.Y = HeaderBar.Rows + 1;
            page.Width = Dim.Fill();
            page.Height = Dim.Fill(FooterView.Rows);
        }

        _footer = new FooterView(player, waves) { X = 0, Y = Pos.AnchorEnd(FooterView.Rows), Width = Dim.Fill(), Height = FooterView.Rows };
        _help.X = 0;
        _help.Y = HeaderBar.Rows + 1;
        _help.Width = Dim.Fill();
        _help.Height = Dim.Fill(FooterView.Rows);
        _help.Visible = false;

        Add(_header, _tabs);
        Add(_pages);
        Add(_help);
        Add(_footer);

        _tabs.SelectedChanged += ShowTab;
        ShowTab(0);
        _tabs.Selected = 1;

        _header.Search.TextChanged += (_, _) => OnSearchTextChanged();
        _header.SyncClicked += OfferResync;
        _search.PlayRequested += Play;
        _search.QueueRequested += OnQueueRequested;
        _search.Notice += Notice;
        _search.GenreChosen += genre =>
        {
            _tabs.Selected = 1;
            _library.SelectGenre(genre.Name);
            _library.Artists.SetFocus();
        };
        search.Changed += outcome => _app.Invoke(() => _search.Show(outcome));
        _library.Albums.PlayRequested += Play;
        _library.Albums.QueueRequested += OnQueueRequested;
        _library.Albums.VisibleAlbumsChanged += _refresher.SetVisible;
        _footer.ArtistClicked += album => ShowInLibrary(album, scrollToAlbum: false);
        _footer.AlbumClicked += album => ShowInLibrary(album, scrollToAlbum: true);
        _search.ArtistChosen += artist =>
        {
            _tabs.Selected = 1;
            _library.SelectArtist(artist.Id);
            _library.Artists.SetFocus();
        };

        // The service raises events on background threads; everything touching views goes through the UI thread.
        catalog.StatusChanged += _ => _app.Invoke(OnStatusChanged);
        catalog.CatalogChanged += _ => _app.Invoke(OnCatalogChanged);
        catalog.PromptRequested += p => _app.Invoke(() => OnPrompt(p));
        catalog.AlbumRefreshed += _ => _app.Invoke(() => _library.Albums.SetNeedsDraw());
        CoverArt.Source.Updated += () => Interlocked.Exchange(ref _coversDirty, 1);

        _app.Keyboard.KeyDown += OnGlobalKey;
        // Esc is used inside the app (close, back, clear); only Ctrl+Q quits.
        if (_app.Keyboard.KeyBindings.TryGet(Key.Esc, out _))
        {
            _app.Keyboard.KeyBindings.Remove(Key.Esc);
        }

        _app.AddTimeout(FrameInterval, OnTick);
        UpdateTabTitle();
        player.Changed += () => _app.Invoke(OnPlayerChanged);
        player.Notice += text => _app.Invoke(() => Notice(text));
        _queue.Notice += Notice;
        _queue.ClearRequested += () => _tabs.ShowPrompt(
            "Clear the whole queue? Playback stops.",
            new PromptChoice('Y', "Clear", _player.ClearQueue),
            new PromptChoice('N', "No", () => { }));
        _queue.LoadRequested += (saved, replace) =>
        {
            if (!replace)
            {
                _player.PlaySaved(saved, false);
                return;
            }

            // Loading a queue restarts the stream, which is audible as a gap: say so before doing it.
            _tabs.ShowPrompt(
                $"Replace the queue with \"{saved.Name}\"? The music restarts (not gapless).",
                new PromptChoice('Y', "Replace", () => _player.PlaySaved(saved, true)),
                new PromptChoice('N', "No", () => { }));
        };
        if (_waves is not null)
        {
            _waves.Updated += () => _app.Invoke(() => _footer.SetNeedsDraw());
        }

        ApplyCatalog();
        _library.Artists.SetFocus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refresher.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ShowInLibrary(CatalogAlbum album, bool scrollToAlbum)
    {
        if (album.ArtistId is not { } artistId)
        {
            Notice("This album has no album artist to open.");
            return;
        }

        _tabs.Selected = 1;
        _library.SelectArtist(artistId);
        if (scrollToAlbum)
        {
            _library.Albums.ScrollToAlbum(album.Id);
        }

        _library.Artists.SetFocus();
    }

    private void ShowTab(int index)
    {
        if (index == 0 && _currentTab > 0)
        {
            _tabBeforeSearch = _currentTab;
        }

        _currentTab = index;
        for (var i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visible = i == index;
        }

        // Hidden views no longer paint, so what they showed must be withdrawn explicitly: their sixel images, their
        // pending cover requests and their on-view refreshes.
        if (index != 1)
        {
            SixelCovers.Release(_library.Albums);
            CoverArt.Source.Want(_library.Albums, Array.Empty<CoverKey>());
            _refresher.SetVisible(Array.Empty<CatalogAlbum>());
            _library.Albums.ForgetVisible();
        }

        if (index == 2)
        {
            _queue.Refresh();
        }

        if (index != 0)
        {
            SixelCovers.Release(_search.List);
            CoverArt.Source.Want(_search.List, Array.Empty<CoverKey>());
        }

        SetNeedsDraw();
    }

    private void Play(CatalogAlbum album, IReadOnlyList<CatalogTrack> tracks, int trackIndex)
    {
        if (tracks.Count == 0)
        {
            Notice("This album's tracks are still loading.");
            return;
        }

        _player.Play(album, tracks, trackIndex);
    }

    private void OnQueueRequested(QueueAction action, CatalogAlbum album, IReadOnlyList<CatalogTrack> tracks, int trackIndex)
    {
        if (tracks.Count == 0)
        {
            Notice("This album's tracks are still loading.");
            return;
        }

        _player.Enqueue(action, album, tracks);
    }

    private string _tabTitle = "";

    /// <summary>The terminal tab title: "cLiMS", with ▶ in front while a track plays and ⏸ while it is paused.</summary>
    private void UpdateTabTitle()
    {
        var title = _player.Current is null ? "cLiMS" : (_player.IsPlaying ? "▶ cLiMS" : "⏸ cLiMS");
        if (title == _tabTitle)
        {
            return;
        }

        _tabTitle = title;
        try
        {
            Console.Title = title;
        }
        catch (IOException)
        {
            // No console to title (redirected output): nothing to do.
        }
    }

    private void OnPlayerChanged()
    {
        UpdateTabTitle();
        _footer.SetNeedsDraw();
        _queue.Refresh();
        WantWaveforms();
    }

    /// <summary>Asks the waveform service for the current track first and the next queue entry ahead of time.</summary>
    private void WantWaveforms()
    {
        if (_waves is null)
        {
            return;
        }

        var current = _player.Current?.Track.Id;
        var queue = _player.Queue;
        var next = _player.QueueIndex >= 0 && _player.QueueIndex + 1 < queue.Count ? queue[_player.QueueIndex + 1].Track.Id : (int?)null;
        var signature = $"{current}:{next}";
        if (signature == _waveSignature)
        {
            return;
        }

        _waveSignature = signature;
        _waves.Want(new[] { current, next }.Where(i => i is not null).Select(i => i!.Value).ToArray());
    }

    private void Notice(string text)
    {
        _tabs.ShowNotice(text);
        _noticeUntil = DateTime.UtcNow + NoticeDuration;
    }

    private void OnStatusChanged()
    {
        _header.Refresh();
        ApplyCatalog();

        // Failures and cancellations explain themselves in the status detail; make sure the user sees it.
        var status = _catalog.Status;
        if (status.Detail is { Length: > 0 } detail && status.State is SyncState.NotSynced or SyncState.Offline)
        {
            Notice(detail);
        }
    }

    private void OnCatalogChanged()
    {
        _refresher.Reset();
        ApplyCatalog();

        // The index changed under the search results (a sync finished, or tracks arrived): search again.
        if (_header.Search.Text.Trim().Length > 0)
        {
            _searchService.Query(_header.Search.Text);
        }
    }

    /// <summary>Gives the tabs the current catalogue, or an explanation when there is none.</summary>
    private void ApplyCatalog()
    {
        var catalog = _catalog.Current;
        var status = _catalog.Status;
        var message = status.State switch
        {
            SyncState.Syncing => "Syncing the library…",
            SyncState.Offline => "The server cannot be reached and there is no saved library yet. Press F5 to retry.",
            _ => "Library not synced yet. Press F5 to sync.",
        };
        _library.SetCatalog(catalog, message);
        _search.SetCatalog(catalog);
    }

    private void OnPrompt(SyncPrompt prompt)
    {
        var text = prompt.Reason == SyncPromptReason.FirstBuild
            ? $"Library not synced yet ({prompt.Detail}). Sync now?"
            : $"Server library changed ({prompt.Detail}). Resync now?";
        _tabs.ShowPrompt(text, new PromptChoice('Y', "Sync now", StartSync), new PromptChoice('L', "Later", _catalog.DeferSync));
    }

    /// <summary>F5 or a click on the status: ask whether to resync, or whether to cancel a running sync.</summary>
    private void OfferResync()
    {
        var status = _catalog.Status;
        if (status.State == SyncState.Demo)
        {
            Notice("Demo mode: there is no server to sync with.");
        }
        else if (status.State == SyncState.Syncing)
        {
            _tabs.ShowPrompt("A sync is running. Cancel it?", new PromptChoice('Y', "Cancel sync", _catalog.CancelSync), new PromptChoice('N', "Keep going", () => { }));
        }
        else
        {
            _tabs.ShowPrompt("Resync the library now?", new PromptChoice('Y', "Resync", StartSync), new PromptChoice('N', "No", () => { }));
        }
    }

    private void StartSync() => _ = RunSyncAsync();

    private async Task RunSyncAsync()
    {
        try
        {
            await _catalog.SyncAsync();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex); // SyncAsync reports LMS failures through the status; anything else is a bug worth a log line
        }
    }

    private void OnSearchTextChanged()
    {
        // Switch to the Search tab as soon as the user types. The local search is fast enough to run on every keystroke;
        // only the server part (while the index is incomplete) is debounced, inside the service.
        _tabs.Selected = 0;
        _searchService.Query(_header.Search.Text);
    }

    private bool OnTick()
    {
        var now = DateTime.UtcNow;
        // Sixel support is detected asynchronously a moment after startup; repaint covers once it is known.
        if (!_sixelSeen && SixelCovers.Available)
        {
            _sixelSeen = true;
            _library.Albums.SetNeedsDraw();
            _search.List.SetNeedsDraw();
        }

        if (Interlocked.Exchange(ref _coversDirty, 0) == 1)
        {
            _library.Albums.SetNeedsDraw();
            _search.List.SetNeedsDraw();
        }

        if (_noticeUntil != DateTime.MinValue && now > _noticeUntil)
        {
            _noticeUntil = DateTime.MinValue;
            if (_tabs.Choices.Count == 0)
            {
                _tabs.ClearMessage();
            }
        }

        _player.Tick(now - _lastTick);
        _lastTick = now;
        _footer.ShowCover = Viewport.Width >= MinWidth;
        _footer.ShowWaveform = Viewport.Height >= MinHeight;
        _footer.SetNeedsDraw();
        _header.Refresh();
        return true;
    }

    private void OnGlobalKey(object? sender, Key key)
    {
        if (key == Key.Q.WithCtrl)
        {
            _app.RequestStop();
            key.Handled = true;
            return;
        }

        // Next / previous track work from everywhere, even while typing in a text field.
        if (key == Key.N.WithAlt || key == Key.P.WithAlt)
        {
            if (key == Key.N.WithAlt)
            {
                _player.Next();
            }
            else
            {
                _player.Previous();
            }

            key.Handled = true;
            return;
        }

        var searching = _header.Search.HasFocus;
        if (searching)
        {
            if (key == Key.Esc)
            {
                LeaveSearch();
                key.Handled = true;
            }
            else if (key == Key.Enter || key == Key.CursorDown)
            {
                FocusPage();
                key.Handled = true;
            }

            return;
        }

        // Typing a queue name: the text field gets every key except Esc and Enter.
        if (_queue.Naming)
        {
            if (key == Key.Esc)
            {
                _queue.CancelNaming();
                key.Handled = true;
            }
            else if (key == Key.Enter)
            {
                _queue.ConfirmName();
                key.Handled = true;
            }

            return;
        }

        if (_helpVisible && key == Key.Esc)
        {
            ToggleHelp();
            key.Handled = true;
            return;
        }

        // Esc in the results clears the search and goes back to the tab the user came from.
        if (key == Key.Esc && _tabs.Selected == 0 && _tabs.Choices.Count == 0)
        {
            LeaveSearch();
            key.Handled = true;
            return;
        }

        var ch = key.AsRune.Value is > 32 and < 0xFFFF ? (char)key.AsRune.Value : '\0';

        // A pending question (sync now?) takes its answer keys before anything else.
        if (_tabs.Choices.Count > 0)
        {
            if ((key == Key.Enter && _tabs.Answer(_tabs.Choices[0].Key))
                || (key == Key.Esc && _tabs.Answer(_tabs.Choices[^1].Key))
                || (ch != '\0' && _tabs.Answer(ch)))
            {
                key.Handled = true;
                return;
            }
        }

        if (ch == '?')
        {
            ToggleHelp();
            key.Handled = true;
            return;
        }

        // Filtering the left list of the Library: the text field gets every key except Esc (close) and Enter / Down (to the list).
        if (_library.Filtering && _library.Filter.Field.HasFocus)
        {
            if (key == Key.Esc)
            {
                _library.CloseFilter();
                key.Handled = true;
            }
            else if (key == Key.Enter || key == Key.CursorDown)
            {
                _library.Artists.SetFocus();
                key.Handled = true;
            }

            return;
        }

        if (key == Key.F.WithCtrl && _tabs.Selected == 1)
        {
            _library.BeginFilter();
            key.Handled = true;
            return;
        }

        if (_tabs.Selected == 2 && _queue.List.HasFocus && _queue.HandleKey(key))
        {
            key.Handled = true;
            return;
        }

        // Letters are type-ahead in the left list, and a/n mean add / play next in the album pane.
        var typeAhead = _library.Artists.HasFocus;
        var albumKeys = _library.Albums.HasFocus;
        switch (ch)
        {
            case '/':
                // Deferred, otherwise the same key press is also typed into the freshly focused field.
                _app.AddTimeout(TimeSpan.FromMilliseconds(1), () =>
                {
                    _header.Search.SetFocus();
                    return false;
                });
                break;
            case '1' or '2' or '3' when !typeAhead:
                _tabs.Selected = ch - '1';
                FocusPage();
                break;
            case '+' or '=':
                _player.SetVolume(_player.Volume + 5);
                break;
            case '-':
                _player.SetVolume(_player.Volume - 5);
                break;
            default:
                if (key == Key.Space)
                {
                    _player.TogglePlay();
                }
                else if (key == Key.CursorRight)
                {
                    _player.Seek(TimeSpan.FromSeconds(10));
                }
                else if (key == Key.CursorLeft)
                {
                    _player.Seek(TimeSpan.FromSeconds(-10));
                }
                else if (key == Key.F3)
                {
                    do
                    {
                        CoverArt.Mode = (CoverMode)(((int)CoverArt.Mode + 1) % 4);
                    }
                    while (CoverArt.Mode == CoverMode.Sixel && !SixelCovers.Available);

                    _library.Albums.SetNeedsDraw();
                }
                else if (key == Key.F4)
                {
                    CoverArt.Large = !CoverArt.Large;
                    _library.Albums.Relayout();
                }
                else if (key == Key.F5)
                {
                    OfferResync();
                }
                else if (key == Key.F6)
                {
                    _library.CycleDimension();
                }
                else
                {
                    return;
                }

                break;
        }

        key.Handled = true;
    }

    private void ToggleHelp()
    {
        _helpVisible = !_helpVisible;
        _help.Visible = _helpVisible;
        if (_helpVisible)
        {
            // Sixel images sit above any text, so the pages underneath must stop showing theirs while help is open.
            foreach (var page in _pages)
            {
                page.Visible = false;
            }

            SixelCovers.Release(_library.Albums);
            SixelCovers.Release(_search.List);
            CoverArt.Source.Want(_library.Albums, Array.Empty<CoverKey>());
            CoverArt.Source.Want(_search.List, Array.Empty<CoverKey>());
            _refresher.SetVisible(Array.Empty<CatalogAlbum>());
            _help.SetFocus();
        }
        else
        {
            ShowTab(_currentTab);
            FocusPage();
            _library.Albums.SetNeedsDraw();
            _search.List.SetNeedsDraw();
        }

        SetNeedsDraw();
    }

    private void LeaveSearch()
    {
        _header.Search.Text = "";
        _searchService.Query("");
        _tabs.Selected = _tabBeforeSearch;
        FocusPage();
    }

    private void FocusPage()
    {
        switch (_tabs.Selected)
        {
            case 0:
                _search.List.SetFocus();
                break;
            case 1:
                _library.Albums.SetFocus();
                break;
            case 2:
                _queue.Refresh();
                _queue.List.SetFocus();
                break;
        }
    }
}
