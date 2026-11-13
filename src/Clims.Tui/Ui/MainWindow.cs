using Clims.Tui.Settings;
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
    private readonly RecentTab _recent;
    private readonly GenresTab _genres;
    private readonly OnlineTab _online;
    private readonly WaveformService? _waves;
    private readonly bool _waveformEnabled;
    private readonly LyricsFeed? _lyrics;
    private string _waveSignature = "";
    private bool _helpVisible;
    private bool _infoVisible;
    private bool _menuVisible;
    private readonly ContextMenu _menu = new();
    private readonly ClickShield _shield = new();
    private readonly AlbumInfoOverlay _info = new();
    private readonly HelpOverlay _help = new();
    private readonly SettingsOverlay? _settings;
    private bool _settingsVisible;
    private int _currentTab = -1;
    private int _tabBeforeSearch = 1;
    private DateTime _lastTick = DateTime.UtcNow;
    private DateTime _lastSlowDraw = DateTime.MinValue;
    private DateTime _noticeUntil = DateTime.MinValue;
    private int _coversDirty;
    private bool _sixelSeen;

    /// <summary>Set when the user pressed F2: the host closes the window and shows the connect screen.</summary>
    public bool ChangeServerRequested { get; private set; }

    /// <summary>Set with <see cref="ChangeServerRequested"/> when the user chose a server in the settings: connect to it without the connect screen (its key: UUID or address).</summary>
    public string? RequestedServer { get; private set; }

    public MainWindow(
        IApplication app,
        ICatalogService catalog,
        ISearchService search,
        IPlayerController player,
        SavedQueueStore queues,
        WaveformService? waves = null,
        RecentAlbumsSource? recentAlbums = null,
        LyricsFeed? lyrics = null,
        WaveformStyle waveformStyle = WaveformStyle.Blocks,
        bool waveformEnabled = true,
        StartupView startupView = StartupView.Library,
        SettingsStore? settings = null,
        string? connectedUrl = null,
        OnlineSource? online = null)
    {
        _waveformEnabled = waveformEnabled;
        _waves = waveformEnabled ? waves : null;
        _lyrics = lyrics;
        _app = app;
        _catalog = catalog;
        _searchService = search;
        _player = player;
        _refresher = new AlbumRefresher(catalog);

        Title = "";
        BorderStyle = LineStyle.None;
        SetScheme(Theme.Dark);

        _header = new HeaderBar(player, () => catalog.Status) { X = 0, Y = 0 };
        _tabs = new TabStrip("Search", "Library", "Queue", "Recently added", "Genres", "Online") { X = 0, Y = HeaderBar.Rows, Width = Dim.Fill(), Height = 1 };

        _search = new SearchTab();
        _library = new LibraryTab();
        _queue = new QueueTab(player, queues);
        _recent = new RecentTab(recentAlbums ?? ((_, _) => Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>())));
        _genres = new GenresTab();
        _online = new OnlineTab(online, action => _app.Invoke(action));
        _pages = new View[] { _search, _library, _queue, _recent, _genres, _online };
        foreach (var page in _pages)
        {
            page.X = 0;
            page.Y = HeaderBar.Rows + 1;
            page.Width = Dim.Fill();
            page.Height = Dim.Fill(FooterView.Rows);
        }

        _footer = new FooterView(player, _waves) { X = 0, Y = Pos.AnchorEnd(FooterView.Rows), Width = Dim.Fill(), Height = FooterView.Rows, WaveformStyle = waveformStyle, ShowVersion = settings?.Current.Ui.ShowVersion ?? true };
        _help.X = 0;
        _help.Y = HeaderBar.Rows + 1;
        _help.Width = Dim.Fill();
        _help.Height = Dim.Fill(FooterView.Rows);
        _help.Visible = false;
        if (settings is not null)
        {
            var model = new SettingsModel(settings, connectedUrl);
            _settings = new SettingsOverlay(model) { X = 0, Y = HeaderBar.Rows + 1, Width = Dim.Fill(), Height = Dim.Fill(FooterView.Rows), Visible = false };
            model.Changed += OnSettingsChanged;
            model.AddRequested += RequestServerChange;
            model.SwitchRequested += key =>
            {
                RequestedServer = key;
                RequestServerChange();
            };
            _settings.CloseRequested += ToggleSettings;
            _settings.PickColor = (name, colour) => ColorDialog.Show(_app, name, colour);
        }

        // The album view covers the whole screen.
        _info.X = 0;
        _info.Y = 0;
        _info.Width = Dim.Fill();
        _info.Height = Dim.Fill();
        _info.Visible = false;

        Add(_header, _header.Search, _tabs);
        Add(_pages);
        Add(_help);
        if (_settings is not null)
        {
            Add(_settings);
        }

        Add(_footer);
        Add(_info);
        _shield.Visible = false;
        Add(_shield);
        _menu.Visible = false;
        Add(_menu);
        _menu.Chosen += RunMenuEntry;
        _shield.Clicked += CloseMenu;
        _library.Albums.ContextRequested += OnAlbumContext;
        _recent.Albums.ContextRequested += OnAlbumContext;
        _library.GroupContextRequested += OnGroupContext;
        _search.List.ContextRequested += (item, at) => OpenMenu(
            [new MenuEntry("Play", _search.List.ActivateSelected), new MenuEntry("Enqueue", () => _search.List.RequestQueue(QueueAction.Add))],
            at);
        _footer.SourceName = _info.SourceName = SourceOf;
        _info.Player = player;
        _footer.CoverClicked += OpenInfo;
        _footer.StationClicked += () =>
        {
            _tabs.Selected = 5;
            _online.ShowPlayed();
            FocusPage();
        };
        _info.CloseRequested += CloseInfo;

        _tabs.SelectedChanged += ShowTab;
        _tabs.TabClicked += _ => FocusPage();   // like the number keys: the page gets the keyboard, not the search box
        ShowTab(0);
        _tabs.Selected = startupView.TabIndex();

        _header.Search.TextChanged += (_, _) => OnSearchTextChanged();
        _header.SyncClicked += OfferResync;
        _header.ServerClicked += RequestServerChange;
        _header.SettingsClicked += ToggleSettings;
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
        _recent.Albums.PlayRequested += Play;
        _recent.Albums.QueueRequested += OnQueueRequested;
        _recent.Albums.VisibleAlbumsChanged += _refresher.SetVisible;
        foreach (var albums in new[] { _library.Albums, _recent.Albums })
        {
            albums.ArtistClicked += album => ShowInLibrary(album, scrollToAlbum: false);
            albums.GenreClicked += genre =>
            {
                _tabs.Selected = 1;
                _library.CloseFilter();
                _library.SelectGenre(genre);
                _library.Artists.SetFocus();
            };
            albums.YearClicked += year =>
            {
                _tabs.Selected = 1;
                _library.CloseFilter();
                _library.SelectYear(year);
                _library.Artists.SetFocus();
            };
        }

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
        catalog.AlbumRefreshed += _ => _app.Invoke(() =>
        {
            if (_infoVisible)
            {
                return;
            }

            _library.Albums.SetNeedsDraw();
            _recent.Albums.SetNeedsDraw();
        });
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
        if (lyrics is not null)
        {
            lyrics.Changed += () => _app.Invoke(PushLyrics);
        }

        player.Notice += text => _app.Invoke(() => Notice(text));
        _queue.Notice += Notice;
        _online.Notice += Notice;
        _genres.Notice += Notice;
        _genres.RandomRequested += OnRandomRequested;
        _genres.ArtistChosen += albumId =>
        {
            if (_catalog.Current?.Album(albumId) is { } album)
            {
                ShowInLibrary(album, scrollToAlbum: false);
            }
        };
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
            _waves.Updated += () => _app.Invoke(() => { if (!_infoVisible) { _footer.SetNeedsDraw(); } });
        }

        ApplyCatalog();
        _library.Artists.SetFocus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _recent.Cancel();
            _refresher.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>True while a radio stream plays: it has no tracks to skip and no position to seek to.</summary>
    /// <summary>Where a streamed track comes from. Spotty and SpotOn both use Spotify addresses: the app it was played from tells which.</summary>
    private string? SourceOf(NowPlaying now) =>
        now.Source == "Spotify" && _online.PlayedApp is { Command: "spotty" or "spoton" } app ? app.Name : now.Source;

    private bool OnRadio => _player.Current?.Station is not null;

    private void RequestServerChange()
    {
        ChangeServerRequested = true;
        _app.RequestStop();
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
        if (_settingsVisible)
        {
            // A tab was clicked while the settings page is open: the page gives way to it.
            _settingsVisible = false;
            _settings!.Visible = false;
        }

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

        if (index == 5)
        {
            _online.Show();
        }

        if (index == 3)
        {
            _recent.Albums.ResetCovers();
            _recent.Refresh();
        }
        else
        {
            _recent.Cancel();
            SixelCovers.Release(_recent.Albums);
            CoverArt.Source.Want(_recent.Albums, Array.Empty<CoverKey>());
            _recent.Albums.ForgetVisible();
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

    /// <summary>The terminal tab title: "cLiMS", or "cLiMS ▶ - Track name" (⏸ while paused) once a track is loaded.</summary>
    private void UpdateTabTitle()
    {
        var title = _player.Current is { } now ? $"cLiMS {(_player.IsPlaying ? "▶" : "⏸")} - {now.Track.Title}" : "cLiMS";
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
        if (_infoVisible)
        {
            if (_player.Current is { } playing)
            {
                _info.Show(playing, _catalog.Current);
                PushQueue();
                _lyrics?.Show(playing);
                PushLyrics();
            }
            else
            {
                CloseInfo();
            }
        }

        if (!_infoVisible)
        {
            _footer.SetNeedsDraw();
            _queue.Refresh();
        }

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
        var signature = $"{current}:{next}";   // streams have negative ids: nothing to analyse
        if (signature == _waveSignature)
        {
            return;
        }

        _waveSignature = signature;
        _waves.Want(new[] { current, next }.Where(i => i is > 0).Select(i => i!.Value).ToArray());
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
        if (status.Detail is { Length: > 0 } detail && status.State is SyncState.NotSynced or SyncState.Offline or SyncState.LoginRequired)
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
            SyncState.LoginRequired => "The server asks for a username and password. Press F2 to enter them.",
            _ => "Library not synced yet. Press F5 to sync.",
        };
        _library.SetCatalog(catalog, message);
        _recent.SetCatalog(catalog, message);
        _genres.SetCatalog(catalog, message);
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
        var now = DateTimeOffset.UtcNow;
        var (_, label, _) = HeaderBar.Describe(status, now);
        if (status.State == SyncState.Demo)
        {
            Notice("Demo mode: there is no server to sync with.");
        }
        else if (status.State == SyncState.Syncing)
        {
            _tabs.ShowPrompt(label + ". Cancel it?", new PromptChoice('Y', "Cancel sync", _catalog.CancelSync), new PromptChoice('N', "Keep going", () => { }));
        }
        else
        {
            var text = status.State == SyncState.Synced ? $"Synced {HeaderBar.Age(status.SyncedAt, now)}" : label;
            _tabs.ShowPrompt(text + ", resync now?", new PromptChoice('Y', "Resync", StartSync), new PromptChoice('N', "No", () => { }));
        }
    }

    /// <summary>A random queue from the Genres tab: appending just happens, replacing asks first (the music restarts).</summary>
    private void OnRandomRequested(IReadOnlyList<(CatalogAlbum Album, CatalogTrack Track)> items, bool replace, string what)
    {
        if (!replace || _player.Queue.Count == 0)
        {
            _player.PlayTracks(items, replace);
            return;
        }

        _tabs.ShowPrompt(
            $"Replace the queue with {items.Count} random tracks of {what}? The music restarts (not gapless).",
            new PromptChoice('Y', "Replace", () => _player.PlayTracks(items, true)),
            new PromptChoice('N', "No", () => { }));
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

    private (int W, int H) _lastSize;
    private DateTime _resizeSettledAt = DateTime.MinValue;
    private int _resizeRefreshes;

    /// <summary>
    /// A terminal erases its sixel images when the window is resized, while the user is still dragging and for a moment after.
    /// Once the size has stopped changing, send them again (twice, a little apart, as the terminal may finish later than we do).
    /// </summary>
    private void RedrawCoversAfterResize(DateTime now)
    {
        var size = (Viewport.Width, Viewport.Height);
        if (size != _lastSize)
        {
            _lastSize = size;
            _resizeRefreshes = 2;
            _resizeSettledAt = now + TimeSpan.FromMilliseconds(400);
            return;
        }

        if (_resizeRefreshes == 0 || now < _resizeSettledAt)
        {
            return;
        }

        _resizeRefreshes--;
        _resizeSettledAt = now + TimeSpan.FromMilliseconds(1000);
        SixelCovers.Refresh();
        _info.SetNeedsDraw();
        _library.Albums.SetNeedsDraw();
        _recent.Albums.SetNeedsDraw();
        _search.List.SetNeedsDraw();
        _footer.SetNeedsDraw();
    }

    private bool OnTick()
    {
        var now = DateTime.UtcNow;
        // Sixel support is detected asynchronously a moment after startup; repaint covers once it is known.
        if (!_sixelSeen && SixelCovers.Available)
        {
            _sixelSeen = true;
            _library.Albums.SetNeedsDraw();
            _recent.Albums.SetNeedsDraw();
            _search.List.SetNeedsDraw();
        }

        RedrawCoversAfterResize(now);

        if (Interlocked.Exchange(ref _coversDirty, 0) == 1)
        {
            _info.SetNeedsDraw();
            if (!_infoVisible)
            {
            _library.Albums.SetNeedsDraw();
            _recent.Albums.SetNeedsDraw();
            _search.List.SetNeedsDraw();
            }
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
        _footer.ShowWaveform = _waveformEnabled && Viewport.Height >= MinHeight;
        // While the album view is open the screen under it stays still: repainting it would wipe the cover image (a sixel is erased by any text written over it).
        // Once a second is enough (the clock shows whole seconds; changes of state redraw at once). Every redraw makes the driver
        // reposition the terminal's cursor, which restarts its blink: ten a second made a text field's cursor flicker.
        var secondPassed = now - _lastSlowDraw >= TimeSpan.FromSeconds(1);
        if (secondPassed)
        {
            _lastSlowDraw = now;
            if (!_infoVisible)
            {
                _footer.SetNeedsDraw();
            }

            _header.Refresh();
        }

        if (_infoVisible)
        {
            _info.Tick(_player.Elapsed, now);
        }

        return true;
    }

    private void OnGlobalKey(object? sender, Key key)
    {
        // The colour picker dialog is modal and takes its own keys.
        if (_settings?.PickerOpen == true)
        {
            return;
        }

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
                if (!OnRadio) { _player.Next(); }
            }
            else
            {
                if (!OnRadio) { _player.Previous(); }
            }

            key.Handled = true;
            return;
        }

        if (_settings is not null && SettingsOverlay.IsToggleKey(key) && !_settingsVisible && !_infoVisible && !_menuVisible && !_helpVisible)
        {
            ToggleSettings();
            key.Handled = true;
            return;
        }

        // While the settings page is open it takes every key (F10 and Esc close it).
        if (_settingsVisible)
        {
            _settings!.HandleKey(key);
            key.Handled = true;
            return;
        }

        if (_menuVisible)
        {
            if (key == Key.CursorDown)
            {
                _menu.MoveSelection(1);
            }
            else if (key == Key.CursorUp)
            {
                _menu.MoveSelection(-1);
            }
            else if (key == Key.Enter)
            {
                RunMenuEntry();
            }
            else if (key == Key.Esc)
            {
                CloseMenu();
            }

            key.Handled = true;
            return;
        }

        if (_infoVisible)
        {
            var c = key.AsRune.Value;
            if (key == Key.Esc || key == Key.Enter || c is 'q' or 'Q')
            {
                CloseInfo();
            }
            else if (key == Key.Space)
            {
                _player.TogglePlay();
            }
            else if (c is '+' or '=')
            {
                _player.SetVolume(_player.Volume + 5);
            }
            else if (c == '-')
            {
                _player.SetVolume(_player.Volume - 5);
            }
            else if (key == Key.CursorRight || key == Key.CursorLeft)
            {
                if (!OnRadio) { _player.Seek(TimeSpan.FromSeconds(key == Key.CursorRight ? 10 : -10)); }
            }
            else if (c is 'r' or 'R')
            {
                _lyrics?.Show(_player.Current, refresh: true);
            }
            else if (c is 'l' or 'L')
            {
                _lyrics?.EnableOnline();
            }
            else if (key == Key.F3)
            {
                CoverArt.SetStyle(CoverPlace.FullScreen, CoverArt.NextStyle(CoverPlace.FullScreen));
                _info.ResetCovers();
                _info.SetNeedsDraw();
            }
            else if (key == Key.F4)
            {
                CoverArt.FullScreenSize = CoverArt.NextSize(CoverArt.FullScreenSize);
                _info.ResetCovers();
                _info.SetNeedsDraw();
            }
            else
            {
                _info.HandleLyricsKey(key);
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

        // Typing the text of a search in an app: same, the text field gets every key except Esc and Enter.
        if (_online.Searching)
        {
            if (key == Key.Esc)
            {
                _online.CancelSearch();
                key.Handled = true;
            }
            else if (key == Key.Enter)
            {
                _online.ConfirmSearch();
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

        if (_tabs.Selected == 4 && _genres.HasFocus && _genres.HandleKey(key))
        {
            key.Handled = true;
            return;
        }

        if (_tabs.Selected == 2 && _queue.List.HasFocus && _queue.HandleKey(key))
        {
            key.Handled = true;
            return;
        }

        if (_tabs.Selected == 5 && _online.List.HasFocus && _online.HandleKey(key))
        {
            key.Handled = true;
            return;
        }

        // Letters are type-ahead in the left list, and a/n mean add / play next in the album pane.
        // Digits switch tabs, except in the middle of typing a name to jump to (a name can start with a digit).
        var typeAhead = (_library.Artists.HasFocus && _library.Artists.Typing) || (_tabs.Selected == 4 && _genres.TypingAhead);
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
            case '1' or '2' or '3' or '4' or '5' or '6' when !typeAhead:
                if (ch == '4' && _tabs.Selected == 3)
                {
                    _recent.Refresh(); // pressing 4 on the tab itself asks the server again
                }

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
                    if (GridArrows)
                    {
                        return; // the grid of albums takes them
                    }

                    if (!OnRadio) { _player.Seek(TimeSpan.FromSeconds(10)); }
                }
                else if (key == Key.CursorLeft)
                {
                    if (GridArrows)
                    {
                        return;
                    }

                    if (!OnRadio) { _player.Seek(TimeSpan.FromSeconds(-10)); }
                }
                else if (key == Key.F3)
                {
                    // The style of the place in front: the full-screen cover while it is open, else the lists. For this session only.
                    var place = _infoVisible ? CoverPlace.FullScreen : CoverPlace.Library;
                    CoverArt.SetStyle(place, CoverArt.NextStyle(place));
                    _library.Albums.ResetCovers();
                    _library.Albums.SetNeedsDraw();
                    _recent.Albums.ResetCovers();
                    _recent.Albums.SetNeedsDraw();
                    _info.SetNeedsDraw();
                }
                else if (key == Key.F4)
                {
                    CoverArt.LibrarySize = CoverArt.NextSize(CoverArt.LibrarySize);
                    _library.Albums.Relayout();
                    _recent.Albums.Relayout();
                }
                else if (key == Key.F7)
                {
                    // Album layout of the Library and Recently added tabs: list or grid. For this session only.
                    CoverArt.Layout = CoverArt.Layout == AlbumLayout.List ? AlbumLayout.Grid : AlbumLayout.List;
                    _library.Albums.LayoutChanged();
                    _recent.Albums.LayoutChanged();
                }
                else if (key == Key.F2)
                {
                    RequestServerChange();
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

    private void OpenInfo()
    {
        if (_infoVisible || _helpVisible || _settingsVisible || _player.Current is not { } now)
        {
            return;
        }

        _infoVisible = true;
        _info.Show(null, null); // forget the last album: the first show always paints
        _info.Show(now, _catalog.Current);
        PushQueue();
        _lyrics?.Show(now);
        PushLyrics();
        // Sixel images sit above any text and cannot be dimmed: while the view is open everything under it draws its covers in cells.
        PaintView.Dimmed = true;
        ReleaseSixels();
        _info.Visible = true;
        _info.SetFocus();
        SetNeedsDraw();
    }

    private void OnAlbumContext(CatalogAlbum album, IReadOnlyList<CatalogTrack> tracks, int index, System.Drawing.Point at)
    {
        if (tracks.Count == 0)
        {
            Notice("This album's tracks are still loading.");
            return;
        }

        // Index -1 is the cover: the whole album. Otherwise the one track (played with the rest of the album queued behind it).
        var from = Math.Max(0, index);
        IReadOnlyList<CatalogTrack> chosen = index >= 0 ? new[] { tracks[index] } : tracks;
        OpenMenu(
            [new MenuEntry("Play", () => Play(album, tracks, from)), new MenuEntry("Enqueue", () => OnQueueRequested(QueueAction.Add, album, chosen, from))],
            at);
    }

    /// <summary>Right click on an artist, genre or year: play or enqueue every track of its albums.</summary>
    private void OnGroupContext(IReadOnlyList<CatalogAlbum> albums, string name, System.Drawing.Point at)
    {
        if (_catalog.Current is not { } catalog)
        {
            return;
        }

        var tracks = albums.SelectMany(a => catalog.TracksOf(a.Id).Select(t => (Album: a, Track: t))).ToList();
        if (tracks.Count == 0)
        {
            Notice("The tracks are still loading.");
            return;
        }

        OpenMenu(
            [new MenuEntry("Play", () => _player.PlayTracks(tracks, true)), new MenuEntry("Enqueue", () => _player.PlayTracks(tracks, false))],
            at);
    }

    private void OpenMenu(IReadOnlyList<MenuEntry> entries, System.Drawing.Point at)
    {
        if (_menuVisible || _infoVisible || _helpVisible || _settingsVisible)
        {
            return;
        }

        _menuVisible = true;
        // A cover under the popup is drawn in cells (a sixel would be painted over the popup); the others stay sharp.
        var (menuW, menuH) = ContextMenu.SizeFor(entries);
        var (menuX, menuY) = ContextMenu.Place(at.X, at.Y, menuW, menuH, Viewport.Width, Viewport.Height);
        PaintView.SixelHole = new System.Drawing.Rectangle(menuX, menuY, menuW, menuH);
        ReleaseSixels();
        _shield.Visible = true;
        _menu.Open(entries, at.X, at.Y, Viewport.Width, Viewport.Height);
        SetNeedsDraw();
    }

    private void CloseMenu()
    {
        if (!_menuVisible)
        {
            return;
        }

        _menuVisible = false;
        _menu.Close();
        PaintView.SixelHole = null;
        _shield.Visible = false;
        ReleaseSixels();
        _footer.SetNeedsDraw();
        _library.Albums.SetNeedsDraw();
        _recent.Albums.SetNeedsDraw();
        _search.List.SetNeedsDraw();
        SetNeedsDraw();
    }

    private void RunMenuEntry()
    {
        var entries = _menu.Entries;
        var chosen = _menu.Selected;
        CloseMenu();
        if (chosen >= 0 && chosen < entries.Count)
        {
            entries[chosen].Run();
        }
    }

    /// <summary>Takes every sixel off the screen and has the views draw again (as cells when a modal view is open, as sixels otherwise).</summary>
    private void ReleaseSixels()
    {
        SixelCovers.Release(_library.Albums);
        SixelCovers.Release(_search.List);
        SixelCovers.Release(_recent.Albums);
        SixelCovers.Release(_footer);
        _library.Albums.ResetCovers();
        _recent.Albums.ResetCovers();
        _search.List.ResetCovers();
        _footer.ResetCovers();
    }

    private void PushQueue() => _info.SetQueue(_player.Queue.Select(e => e.Track.Duration).ToList(), _player.QueueIndex);

    private void PushLyrics()
    {
        if (_lyrics is not null && _infoVisible)
        {
            _info.SetLyrics(_lyrics.State, _lyrics.Result, _lyrics.CanEnableOnline);
        }
    }

    private void CloseInfo()
    {
        if (!_infoVisible)
        {
            return;
        }

        _infoVisible = false;
        _info.Visible = false;
        PaintView.Dimmed = false;
        SixelCovers.Release(_info);
        CoverArt.Source.Want(_info, Array.Empty<CoverKey>());
        ReleaseSixels();
        _footer.SetNeedsDraw();
        _library.Albums.SetNeedsDraw();
        _recent.Albums.SetNeedsDraw();
        _search.List.SetNeedsDraw();
        SetNeedsDraw();
    }

    private void ToggleHelp()
    {
        _helpVisible = !_helpVisible;
        _help.Visible = _helpVisible;
        if (_helpVisible)
        {
            _help.ScrollToTop();
            HidePages();
            _help.SetFocus();
        }
        else
        {
            RestorePages();
        }

        SetNeedsDraw();
    }

    /// <summary>Sixel images sit above any text, so the pages underneath must stop showing theirs while a page of text is open over them.</summary>
    private void HidePages()
    {
        foreach (var page in _pages)
        {
            page.Visible = false;
        }

        SixelCovers.Release(_library.Albums);
        SixelCovers.Release(_search.List);
        SixelCovers.Release(_recent.Albums);
        CoverArt.Source.Want(_library.Albums, Array.Empty<CoverKey>());
        CoverArt.Source.Want(_search.List, Array.Empty<CoverKey>());
        CoverArt.Source.Want(_recent.Albums, Array.Empty<CoverKey>());
        _refresher.SetVisible(Array.Empty<CatalogAlbum>());
    }

    private void RestorePages()
    {
        ShowTab(_currentTab);
        FocusPage();
        _library.Albums.ResetCovers();
        _library.Albums.SetNeedsDraw();
        _recent.Albums.ResetCovers();
        _recent.Albums.SetNeedsDraw();
        _search.List.SetNeedsDraw();
    }

    /// <summary>Opens or closes the settings page (F10 or the gear in the header).</summary>
    private void ToggleSettings()
    {
        if (_settings is null || _infoVisible || _menuVisible || _helpVisible)
        {
            return;
        }

        _settingsVisible = !_settingsVisible;
        _settings.Visible = _settingsVisible;
        if (_settingsVisible)
        {
            _settings.Reset();
            HidePages();
            _settings.SetFocus();
        }
        else
        {
            RestorePages();
        }

        SetNeedsDraw();
    }

    /// <summary>A setting was changed (and saved): what can be applied now is.</summary>
    private void OnSettingsChanged(UserSettings settings)
    {
        CoverArt.Apply(settings.Ui.Covers);
        CoverArt.ApplyLayout(settings.Ui);
        Theme.Apply(settings.Ui.Theme);
        SetScheme(Theme.Dark);
        SetNeedsDraw();
        _footer.WaveformStyle = settings.Ui.Waveform.Style;
        _footer.ShowVersion = settings.Ui.ShowVersion;
        _library.Albums.LayoutChanged();
        _recent.Albums.LayoutChanged();
        // The cover (and a sixel waveform) are the same images in new cells: take them off the screen so they are sent again.
        SixelCovers.Release(_footer);
        _footer.SetNeedsDraw();
    }

    /// <summary>True when the arrow keys belong to the grid of albums (it has the focus) and not to the seek shortcut.</summary>
    private bool GridArrows => CoverArt.Layout == AlbumLayout.Grid && (_library.Albums.HasFocus || _recent.Albums.HasFocus);

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
            case 3:
                _recent.Albums.SetFocus();
                break;
            case 4:
                _genres.FocusFirst();
                break;
            case 5:
                _online.List.SetFocus();
                break;
        }
    }
}
