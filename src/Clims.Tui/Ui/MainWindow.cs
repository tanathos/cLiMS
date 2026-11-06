using Clims.Tui.Data;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
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
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(250);

    private readonly IApplication _app;
    private readonly IPlayerController _player;
    private readonly HeaderBar _header;
    private readonly TabStrip _tabs;
    private readonly View[] _pages;
    private readonly LibraryTab _library;
    private readonly SearchTab _search;
    private readonly FooterView _footer;
    private DateTime _searchDue = DateTime.MaxValue;
    private DateTime _lastTick = DateTime.UtcNow;
    private bool _sixelSeen;

    public MainWindow(IApplication app, ILibrarySource library, IPlayerController player)
    {
        _app = app;
        _player = player;

        Title = "";
        BorderStyle = LineStyle.None;
        SetScheme(Theme.Dark);

        _header = new HeaderBar(player) { X = 0, Y = 0 };
        _tabs = new TabStrip("Search", "Library", "Queue") { X = 0, Y = HeaderBar.Rows, Width = Dim.Fill(), Height = 1 };

        _search = new SearchTab(library);
        _library = new LibraryTab(library);
        var queue = new QueuePlaceholder();
        _pages = new View[] { _search, _library, queue };
        foreach (var page in _pages)
        {
            page.X = 0;
            page.Y = HeaderBar.Rows + 1;
            page.Width = Dim.Fill();
            page.Height = Dim.Fill(FooterView.Rows);
        }

        _footer = new FooterView(player) { X = 0, Y = Pos.AnchorEnd(FooterView.Rows), Width = Dim.Fill(), Height = FooterView.Rows };

        Add(_header, _tabs);
        Add(_pages);
        Add(_footer);

        _tabs.SelectedChanged += ShowTab;
        ShowTab(0);
        _tabs.Selected = 1;

        _header.Search.TextChanged += (_, _) => OnSearchTextChanged();
        _search.PlayRequested += Play;
        _library.Albums.PlayRequested += Play;
        _search.ArtistChosen += artist =>
        {
            _tabs.Selected = 1;
            _library.SelectArtist(artist.Name);
            _library.Artists.SetFocus();
        };

        _app.Keyboard.KeyDown += OnGlobalKey;
        _app.AddTimeout(FrameInterval, OnTick);
        player.Changed += () => _footer.SetNeedsDraw();
        _library.Artists.SetFocus();
    }

    private void ShowTab(int index)
    {
        for (var i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visible = i == index;
        }

        // Hidden views no longer paint, so their sixel images must be withdrawn explicitly.
        if (index != 1)
        {
            SixelCovers.Release(_library.Albums);
        }

        SetNeedsDraw();
    }

    private void Play(AlbumItem album, int trackIndex) => _player.Play(album, trackIndex);

    private void OnSearchTextChanged()
    {
        // Switch to the Search tab as soon as the user types; run the query after a short pause.
        _tabs.Selected = 0;
        _searchDue = DateTime.UtcNow + SearchDebounce;
    }

    private bool OnTick()
    {
        var now = DateTime.UtcNow;
        // Sixel support is detected asynchronously a moment after startup; repaint covers once it is known.
        if (!_sixelSeen && SixelCovers.Available)
        {
            _sixelSeen = true;
            _library.Albums.SetNeedsDraw();
        }

        _player.Tick(now - _lastTick);
        _lastTick = now;
        _footer.ShowCover = Viewport.Width >= MinWidth;
        _footer.ShowWaveform = Viewport.Height >= MinHeight;
        _footer.SetNeedsDraw();
        _header.Refresh();

        if (now >= _searchDue)
        {
            _searchDue = DateTime.MaxValue;
            _search.Query(_header.Search.Text);
        }

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

        var searching = _header.Search.HasFocus;
        if (searching)
        {
            if (key == Key.Esc)
            {
                _header.Search.Text = "";
                _search.Query("");
                FocusPage();
                key.Handled = true;
            }
            else if (key == Key.Enter || key == Key.CursorDown)
            {
                _search.Query(_header.Search.Text);
                _searchDue = DateTime.MaxValue;
                FocusPage();
                key.Handled = true;
            }

            return;
        }

        var typeAhead = _library.Artists.HasFocus;
        var ch = key.AsRune.Value is > 32 and < 0xFFFF ? (char)key.AsRune.Value : '\0';
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
            case 'n' when !typeAhead:
                _player.Next();
                break;
            case 'p' when !typeAhead:
                _player.Previous();
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
                else if (key == Key.F2)
                {
                    _header.Sync = (SyncState)(((int)_header.Sync + 1) % 3);
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
                else
                {
                    return;
                }

                break;
        }

        key.Handled = true;
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
        }
    }

    private sealed class QueuePlaceholder : PaintView
    {
        protected override void Paint() =>
            Put(2, 1, "Queue: custom queues arrive in Step 5.", Theme.Dim);
    }
}
