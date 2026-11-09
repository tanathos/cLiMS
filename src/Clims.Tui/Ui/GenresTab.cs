using System.Globalization;
using Clims.Catalog;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Clims.Tui.Ui;

/// <summary>
/// The Genres explorer: pick a genre, and the next column lists the genres that share at least one track with it; pick
/// one of those and the column after it narrows again, and so on. The artists of the tracks tagged with the whole chain
/// are listed underneath, and a random queue of such tracks can be started from the bar on top.
/// </summary>
public sealed class GenresTab : View
{
    /// <summary>The queue sizes offered, in order.</summary>
    public static readonly int[] Sizes = { 25, 50, 100 };

    private const int MinColumnWidth = 24;
    private const int MaxColumns = 12;

    private readonly List<RowList> _columns = new();
    private readonly List<IReadOnlyList<GenreOption>> _options = new();
    private readonly List<int> _chain = new();
    private readonly ActionBar _bar;
    private readonly ItemsHeader _items;
    private bool _showTracks;
    private readonly Random _random;
    private MusicCatalog? _catalog;
    private GenreExplorer? _explorer;
    private string _emptyMessage = "Library not synced yet. Press F5 to sync.";
    private int _count;
    private int _size = 50;
    private int _filterColumn = -1;
    private string _filter = "";
    private bool _typingFilter;
    private bool _updating;
    private int _lastColumn;

    public GenresTab(Random? random = null)
    {
        _random = random ?? Random.Shared;
        CanFocus = true;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _bar = new ActionBar { X = 0, Y = 0, Width = Dim.Fill(), Height = 1 };
        var rule = new Rule { X = 0, Y = Pos.Func(_ => 1 + ColumnsHeight), Width = Dim.Fill(), Height = 1 };
        _items = new ItemsHeader { X = 0, Y = Pos.Func(_ => 2 + ColumnsHeight), Width = Dim.Fill(), Height = 1 };
        Artists = new RowList { X = 0, Y = Pos.Func(_ => 3 + ColumnsHeight), Width = Dim.Fill(), Height = Dim.Fill() };
        Add(_bar, rule, _items, Artists);
        _items.ModeChosen += tracks =>
        {
            _showTracks = tracks;
            ShowItems();
        };
        _bar.SizeChosen += size =>
        {
            _size = size;
            _bar.Set(ChainText, _size, _explorer is not null);
        };
        _bar.PlayClicked += () => RequestRandom(replace: true);
        _bar.AddClicked += () => RequestRandom(replace: false);
        _bar.ChainClicked += column =>
        {
            FocusColumn(column);
            Recompute(column);
        };
        Artists.RowActivated += row =>
        {
            if (row.Data is ArtistOption a)
            {
                ArtistChosen?.Invoke(a.AlbumId);
            }
            else if (row.Data is CatalogTrack t)
            {
                ArtistChosen?.Invoke(t.AlbumId);
            }
        };
        Rebuild();
    }

    /// <summary>The list under the columns: the artists, or the tracks when the "tracks" count was clicked.</summary>
    public RowList Artists { get; }

    /// <summary>The user opened an artist: the id of one album with a matching track.</summary>
    public event Action<int>? ArtistChosen;

    /// <summary>The user asked for a random queue: the tracks with their albums, replace (true) or append, and the chain as text.</summary>
    public event Action<IReadOnlyList<(CatalogAlbum Album, CatalogTrack Track)>, bool, string>? RandomRequested;

    /// <summary>Something to tell the user.</summary>
    public event Action<string>? Notice;

    /// <summary>The genre chain chosen so far, in order.</summary>
    public IReadOnlyList<int> Chain => _chain;

    /// <summary>The chain as text, for example "Rock + Heavy Metal"; empty when nothing is chosen.</summary>
    public string ChainText => string.Join(" + ", _chain.Select(id => _explorer?.NameOf(id) ?? string.Empty));

    /// <summary>How many tracks a random queue holds.</summary>
    public int Size => _size;

    /// <summary>The genre columns on show, left to right.</summary>
    public IReadOnlyList<RowList> Columns => _columns.Take(_count).ToList();

    /// <summary>True while a filter is being typed (letter keys belong to it).</summary>
    public bool TypingFilter => _typingFilter;

    /// <summary>True when the bottom list shows tracks, false when it shows artists.</summary>
    public bool ShowingTracks => _showTracks;

    /// <summary>Switches the bottom list between artists and tracks.</summary>
    public void ShowTracks(bool tracks)
    {
        _showTracks = tracks;
        ShowItems();
    }

    /// <summary>True when a genre list is in the middle of a type-ahead jump (digits then belong to it, not to the tab keys).</summary>
    public bool TypingAhead => _columns.Take(_count).Any(c => c.Typing);

    private int ColumnsHeight => Math.Max(6, (Frame.Height - 2) - (Frame.Height - 2) / 3);

    private int First => Math.Max(0, _count - Math.Clamp(Frame.Width / MinColumnWidth, 1, Math.Max(1, _count)));

    /// <summary>The catalogue changed (loaded, synced): start again with nothing chosen.</summary>
    public void SetCatalog(MusicCatalog? catalog, string emptyMessage)
    {
        _catalog = catalog;
        _emptyMessage = emptyMessage;
        _explorer = catalog is { TracksLoaded: true } && catalog.Genres.Count > 0 ? GenreExplorer.Build(catalog) : null;
        _chain.Clear();
        _lastColumn = 0;
        ClearFilter();
        Rebuild();
    }

    /// <summary>
    /// Puts the keyboard focus back on the column that had it (the first one for a fresh tab). Coming back to the tab must not
    /// count as choosing in that column, otherwise the genres picked after it would be dropped.
    /// </summary>
    public void FocusFirst()
    {
        if (_count > 0)
        {
            var before = _updating;
            _updating = true;
            try
            {
                _columns[Math.Clamp(_lastColumn, 0, _count - 1)].SetFocus();
            }
            finally
            {
                _updating = before;
            }
        }
        else
        {
            Artists.SetFocus();
        }
    }

    /// <summary>Handles the tab's own keys. Returns true when the key was used.</summary>
    public bool HandleKey(Key key)
    {
        if (_typingFilter)
        {
            return HandleFilterKey(key);
        }

        var active = ActiveColumn();
        if (key == Key.CursorRight && active >= 0 && active + 1 < _count)
        {
            EnterColumn(active + 1);
            return true;
        }

        if (key == Key.CursorLeft || key == Key.Backspace)
        {
            if (active > 0)
            {
                FocusColumn(active - 1);
                Recompute(active - 1);
            }
            else if (active < 0 && _count > 0)
            {
                FocusColumn(Math.Max(0, _count - 2));
            }

            return true;
        }

        if (key == Key.Esc)
        {
            if (_filterColumn >= 0)
            {
                ClearFilter();
                Rebuild();
            }
            else if (_chain.Count > 0)
            {
                _chain.Clear();
                Rebuild();
                FocusColumn(0);
            }

            return true;
        }

        if (key == Key.F.WithCtrl && active >= 0)
        {
            StartFilter(active);
            return true;
        }

        if (key == Key.R.WithAlt)
        {
            RequestRandom(replace: true);
            return true;
        }

        if (key == Key.Q.WithAlt)
        {
            RequestRandom(replace: false);
            return true;
        }

        if (!key.IsCtrl && !key.IsAlt && key.AsRune.Value is '[' or ']')
        {
            var at = Array.IndexOf(Sizes, _size);
            _size = Sizes[Math.Clamp(at + (key.AsRune.Value == ']' ? 1 : -1), 0, Sizes.Length - 1)];
            _bar.Set(ChainText, _size, _explorer is not null);
            return true;
        }

        return false;
    }

    private bool HandleFilterKey(Key key)
    {
        if (key == Key.Esc)
        {
            ClearFilter();
            Rebuild();
            return true;
        }

        if (key == Key.Enter || key == Key.CursorDown)
        {
            _typingFilter = false;
            ShowColumn(_filterColumn, keepSelection: true);
            return key == Key.Enter;
        }

        if (key == Key.Backspace)
        {
            if (_filter.Length > 0)
            {
                _filter = _filter[..^1];
                ShowColumn(_filterColumn, keepSelection: false);
                Recompute(_filterColumn);
            }

            return true;
        }

        if (!key.IsCtrl && !key.IsAlt && key.AsRune.Value is > 31 and < 0xFFFF)
        {
            _filter += (char)key.AsRune.Value;
            ShowColumn(_filterColumn, keepSelection: false);
            Recompute(_filterColumn);
        }

        return true;
    }

    private void StartFilter(int column)
    {
        _filterColumn = column;
        _filter = "";
        _typingFilter = true;
        FocusColumn(column);
        ShowColumn(column, keepSelection: false);
    }

    /// <summary>Moves into a column; when it has no choice yet, its first genre becomes the choice.</summary>
    private void EnterColumn(int column)
    {
        if (column < 0 || column >= _count)
        {
            return;
        }

        FocusColumn(column);
        var list = _columns[column];
        if (list.SelectedRow is null && list.Rows.Count > 1)
        {
            list.SelectIndex(1);
        }
    }

    private void ClearFilter()
    {
        _filterColumn = -1;
        _filter = "";
        _typingFilter = false;
    }

    private int ActiveColumn()
    {
        for (var i = 0; i < _count && i < _columns.Count; i++)
        {
            if (_columns[i].HasFocus)
            {
                return i;
            }
        }

        return -1;
    }

    private void FocusColumn(int index)
    {
        if (index >= 0 && index < _count)
        {
            _columns[index].SetFocus();
        }
    }

    private RowList Column(int index)
    {
        while (_columns.Count <= index)
        {
            var i = _columns.Count;
            var list = new RowList
            {
                X = Pos.Func(_ => ColumnX(i)),
                Y = 1,
                Width = Dim.Func(_ => ColumnWidth(i)),
                Height = Dim.Func(_ => ColumnsHeight),
                TypeAhead = true,
                Visible = false,
                IconFor = r => r.Kind == RowKind.Header ? "⌕" : null,
            };
            list.IconClicked += _ => StartFilter(i);
            list.SelectionChanged += _ =>
            {
                if (!_updating)
                {
                    Recompute(i);
                }
            };
            list.RowActivated += _ =>
            {
                Recompute(i);
                EnterColumn(i + 1);
            };
            list.HasFocusChanged += (_, e) =>
            {
                if (e.NewValue)
                {
                    _lastColumn = i;
                }

                // Only a column that already has a chosen genre: merely clicking the title of the next one must not pick for the user.
                if (e.NewValue && !_updating && i < _chain.Count && i < _count)
                {
                    Recompute(i);
                }
            };
            _columns.Add(list);
            Add(list);
        }

        return _columns[index];
    }

    private int ColumnX(int index)
    {
        var shown = Math.Max(1, _count - First);
        return Math.Max(0, index - First) * (Frame.Width / shown);
    }

    private int ColumnWidth(int index)
    {
        var shown = Math.Max(1, _count - First);
        return index == _count - 1 ? Math.Max(1, Frame.Width - ColumnX(index)) : Frame.Width / shown;
    }

    /// <summary>Rebuilds everything from the chain: the columns up to the next choice, the artists and the bar.</summary>
    private void Rebuild()
    {
        var before = _updating;
        _updating = true;
        try
        {
            if (_explorer is null)
            {
                _count = 0;
                _options.Clear();
                foreach (var c in _columns)
                {
                    c.Visible = false;
                }

                Artists.Rows = new List<Row> { new(_catalog is { TracksLoaded: false } ? "Loading the tracks…" : _emptyMessage, Kind: RowKind.More) };
                _bar.Set("", _size, false);
                _items.Clear();
                return;
            }

            // Column i lists what goes with the first i genres of the chain; the last one is the choice still to make.
            _options.Clear();
            var prefix = new List<int>();
            for (var i = 0; i <= _chain.Count; i++)
            {
                var options = _explorer.Options(prefix);
                if (options.Count == 0 && i > 0)
                {
                    break;
                }

                _options.Add(options);
                if (i < _chain.Count)
                {
                    prefix.Add(_chain[i]);
                }
            }

            _count = Math.Min(_options.Count, MaxColumns);
            for (var i = 0; i < _count; i++)
            {
                Column(i);
            }

            for (var i = 0; i < _columns.Count; i++)
            {
                _columns[i].Visible = i < _count;
            }

            for (var i = 0; i < _count; i++)
            {
                ShowColumn(i, keepSelection: true);
            }

            ShowItems();
            _bar.Set(ChainText, _size, true);
            SetNeedsDraw();
        }
        finally
        {
            _updating = before;
        }
    }

    private IReadOnlyList<GenreOption> Listed(int column)
    {
        var options = _options[column];
        return column == _filterColumn && _filter.Length > 0
            ? options.Where(o => o.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList()
            : options;
    }

    private void ShowColumn(int column, bool keepSelection)
    {
        if (column < 0 || column >= _options.Count)
        {
            return;
        }

        var list = Column(column);
        var shown = Listed(column);
        var title = column == 0 ? $"Genres ({_options[0].Count})" : $"+ {_explorer?.NameOf(_chain[column - 1])} ({_options[column].Count})";
        if (column == _filterColumn)
        {
            title = $"Filter: {_filter}{(_typingFilter ? "▏" : "")}  ({shown.Count})";
        }

        var rows = new List<Row> { new(title, Kind: RowKind.Header) };
        rows.AddRange(shown.Select(o => new Row(o.Name, o.Tracks.ToString(CultureInfo.InvariantCulture), Data: o)));
        var before = _updating;
        _updating = true;
        try
        {
            // A column whose genre is already chosen shows the same rows again: keep it where the user scrolled it.
            var offset = list.Offset;
            list.Rows = rows;
            if (keepSelection && column < _chain.Count)
            {
                list.Offset = offset;
            }

            if (keepSelection && column < _chain.Count)
            {
                var at = shown.ToList().FindIndex(o => o.GenreId == _chain[column]);
                if (at >= 0)
                {
                    list.SelectIndex(at + 1);
                }
            }
            else if (column >= _chain.Count)
            {
                list.ClearSelection();   // nothing chosen here yet: nothing looks chosen
            }
        }
        finally
        {
            _updating = before;
        }
    }

    /// <summary>Column <paramref name="column"/> is the one being worked in: the chain is what is highlighted up to it.</summary>
    private void Recompute(int column)
    {
        if (_explorer is null || column < 0 || column >= _count)
        {
            return;
        }

        var chain = new List<int>();
        for (var i = 0; i <= column; i++)
        {
            if (_columns[i].SelectedRow?.Data is not GenreOption option)
            {
                break;
            }

            chain.Add(option.GenreId);
        }

        if (chain.SequenceEqual(_chain))
        {
            return;
        }

        if (_filterColumn > chain.Count - 1 && !_typingFilter)
        {
            ClearFilter();
        }

        _chain.Clear();
        _chain.AddRange(chain);
        Rebuild();
    }

    private void ShowItems()
    {
        if (_explorer is null || _catalog is null)
        {
            return;
        }

        if (_chain.Count == 0)
        {
            _items.Clear();
            Artists.Rows = new List<Row> { new("Artists appear here once a genre is chosen.", Kind: RowKind.More) };
            return;
        }

        var artists = _explorer.Artists(_chain);
        var matching = _explorer.Match(_chain);
        var note = _explorer.HasAllGenres ? "" : "   ·   F5 to resync: every genre of each track is read then";
        _items.Set($"Items tagged {ChainText}", artists.Count, matching.Count, _showTracks, note);

        if (!_showTracks)
        {
            Artists.Rows = artists.Select(a => new Row(a.Name, a.Tracks == 1 ? "1 track" : $"{a.Tracks:N0} tracks", Data: a)).ToList();
            return;
        }

        var rows = new List<Row>(matching.Count);
        foreach (var t in matching
                     .Select(t => (Track: t, Album: _catalog.Album(t.AlbumId)))
                     .OrderBy(x => x.Track.Artist ?? x.Album?.ArtistName ?? "", StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.Album?.Title ?? "", StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.Track.Disc)
                     .ThenBy(x => x.Track.Number))
        {
            var who = !string.IsNullOrWhiteSpace(t.Track.Artist) ? t.Track.Artist! : t.Album?.ArtistName ?? "";
            var prefix = $"{who} · {t.Album?.Title} · ";
            var genres = _explorer.GenreCount(t.Track.Id);
            rows.Add(new Row(prefix + t.Track.Title, genres == 1 ? "1 genre" : $"{genres} genres", Data: t.Track, Bold: (prefix.Length, t.Track.Title.Length)));
        }

        Artists.Rows = rows;
    }

    /// <summary>The line above the bottom list: what is tagged, and the artists / tracks counts that switch the list.</summary>
    private sealed class ItemsHeader : PaintView
    {
        private string _what = "";
        private string _note = "";
        private int _artists;
        private int _tracks;
        private bool _showTracks;
        private bool _any;
        private (int X0, int X1) _artistsAt;
        private (int X0, int X1) _tracksAt;

        public event Action<bool>? ModeChosen;

        public void Clear()
        {
            _any = false;
            SetNeedsDraw();
        }

        public void Set(string what, int artists, int tracks, bool showTracks, string note)
        {
            (_what, _artists, _tracks, _showTracks, _note, _any) = (what, artists, tracks, showTracks, note, true);
            SetNeedsDraw();
        }

        protected override void Paint()
        {
            _artistsAt = _tracksAt = (0, 0);
            if (!_any)
            {
                return;
            }

            var plain = Theme.Heading;
            var chosen = new Attribute(Theme.Accent, Theme.Bg, TextStyle.Bold);
            var link = new Attribute(Theme.Accent, Theme.Bg, TextStyle.Underline);
            var x = Put(1, 0, Clip(_what, W - 2) + "  ·  ", plain);
            var a = Put(x, 0, $"{_artists:N0} artists", _showTracks ? link : chosen);
            _artistsAt = (x, a);
            x = Put(a, 0, "  ·  ", plain);
            var t = Put(x, 0, $"{_tracks:N0} tracks", _showTracks ? chosen : link);
            _tracksAt = (x, t);
            Put(t, 0, _note, plain);
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && mouse.Position is { } p)
            {
                if (p.X >= _artistsAt.X0 && p.X < _artistsAt.X1)
                {
                    ModeChosen?.Invoke(false);
                    return true;
                }

                if (p.X >= _tracksAt.X0 && p.X < _tracksAt.X1)
                {
                    ModeChosen?.Invoke(true);
                    return true;
                }
            }

            return base.OnMouseEvent(mouse);
        }
    }

    private void RequestRandom(bool replace)
    {
        if (_explorer is null || _catalog is null)
        {
            Notice?.Invoke("The library is not loaded yet.");
            return;
        }

        if (_chain.Count == 0)
        {
            Notice?.Invoke("Pick a genre first.");
            return;
        }

        var items = _explorer.Random(_chain, _size, _random)
            .Select(t => (Album: _catalog.Album(t.AlbumId), Track: t))
            .Where(x => x.Album is not null)
            .Select(x => (x.Album!, x.Track))
            .ToList();
        if (items.Count == 0)
        {
            Notice?.Invoke("No tracks match.");
            return;
        }

        RandomRequested?.Invoke(items, replace, ChainText);
    }

    /// <summary>A thin line between the genre columns and the artists.</summary>
    private sealed class Rule : PaintView
    {
        protected override void Paint() => Put(0, 0, new string('─', W), new Attribute(Theme.FgDim, Theme.Bg));
    }

    /// <summary>The bar on top: the chain as text (each part clickable) and the random-queue controls.</summary>
    private sealed class ActionBar : PaintView
    {
        private readonly List<(int X0, int X1, int Column)> _parts = new();
        private readonly List<(int X0, int X1, int Size)> _sizes = new();
        private (int X0, int X1) _play;
        private (int X0, int X1) _add;
        private string[] _names = Array.Empty<string>();
        private int _size = 50;
        private bool _ready;

        public event Action<int>? SizeChosen;

        public event Action? PlayClicked;

        public event Action? AddClicked;

        public event Action<int>? ChainClicked;

        public void Set(string chain, int size, bool ready)
        {
            _names = chain.Length == 0 ? Array.Empty<string>() : chain.Split(" + ");
            _size = size;
            _ready = ready;
            SetNeedsDraw();
        }

        protected override void Paint()
        {
            _parts.Clear();
            _sizes.Clear();
            _play = _add = (0, 0);
            var back = Theme.BgAlt;
            var dim = new Attribute(Theme.FgDim, back);
            FillRow(0, 0, W, new Attribute(Theme.Fg, back));

            // The controls sit on the right; the chain gets what is left.
            const string label = "Random queue  ";
            const string buttons = "[ ▶ Play ] [ + Add ]";
            var width = label.Length + Sizes.Sum(s => s.ToString(CultureInfo.InvariantCulture).Length + 3) + 2 + buttons.Length + 1;
            var x = W - width;
            var controls = _ready && x > 24;
            if (controls)
            {
                var cx = Put(x, 0, label, dim);
                foreach (var s in Sizes)
                {
                    var next = Put(cx, 0, $"[{s}]", s == _size ? new Attribute(Theme.Accent, back, TextStyle.Bold) : dim);
                    _sizes.Add((cx, next, s));
                    cx = next + 1;
                }

                cx += 1;
                var button = new Attribute(Theme.Fg, Theme.BgSelected);
                var playEnd = Put(cx, 0, "[ ▶ Play ]", button);
                _play = (cx, playEnd);
                var addEnd = Put(playEnd + 1, 0, "[ + Add ]", button);
                _add = (playEnd + 1, addEnd);
            }

            var room = Math.Max(0, (controls ? x : W) - 2);
            if (_names.Length == 0)
            {
                Put(1, 0, Clip(_ready ? "Pick a genre below" : "", room), dim);
                return;
            }

            var px = 1;
            for (var i = 0; i < _names.Length && px < room; i++)
            {
                if (i > 0)
                {
                    px = Put(px, 0, " + ", dim);
                }

                var next = Put(px, 0, Clip(_names[i], Math.Max(0, room - px)), new Attribute(Theme.Fg, back, TextStyle.Bold));
                _parts.Add((px, next, i));
                px = next;
            }
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && mouse.Position is { } p && p.Y == 0)
            {
                foreach (var (x0, x1, size) in _sizes)
                {
                    if (p.X >= x0 && p.X < x1)
                    {
                        SizeChosen?.Invoke(size);
                        return true;
                    }
                }

                if (p.X >= _play.X0 && p.X < _play.X1)
                {
                    PlayClicked?.Invoke();
                    return true;
                }

                if (p.X >= _add.X0 && p.X < _add.X1)
                {
                    AddClicked?.Invoke();
                    return true;
                }

                foreach (var (x0, x1, column) in _parts)
                {
                    if (p.X >= x0 && p.X < x1)
                    {
                        ChainClicked?.Invoke(column);
                        return true;
                    }
                }
            }

            return base.OnMouseEvent(mouse);
        }
    }
}
