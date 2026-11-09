using Clims.Catalog;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Clims.Tui.Ui;

/// <summary>
/// Library tab: a dimension list on the left (type-ahead jumps by name or year), the albums of the selection on the
/// right. Everything comes from the in-memory catalogue, so switching is instant; track lists of visible albums are
/// refreshed live elsewhere.
/// </summary>
public sealed class LibraryTab : View
{
    private MusicCatalog? _catalog;
    private string _emptyMessage = "Library not synced yet. Press F5 to sync.";
    private LibraryDimension _dimension = LibraryDimension.AlbumArtist;

    public LibraryTab()
    {
        CanFocus = true;
        Width = Dim.Fill();
        Height = Dim.Fill();

        var leftWidth = Dim.Func(_ => LeftWidth);
        Header = new DimensionHeader { X = 0, Y = 0, Width = leftWidth, Height = 1 };
        Artists = new RowList
        {
            X = 0, Y = Pos.Func(_ => _filtering ? 2 : 1), Width = leftWidth, Height = Dim.Fill(), TypeAhead = true,
            IconFor = r => r.Data is LibrarySelection { Key: null } ? "⌕" : null,
        };
        Filter = new FilterBar { X = 0, Y = 1, Width = leftWidth, Height = 1, Visible = false };
        Splitter = new SplitterView(this) { X = Pos.Func(_ => LeftWidth), Y = 0, Width = 1, Height = Dim.Fill() };
        Albums = new AlbumsView { X = Pos.Func(_ => LeftWidth + 1), Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        Add(Header, Filter, Artists, Splitter, Albums);
        Artists.IconClicked += _ => BeginFilter();
        Filter.Field.TextChanged += (_, _) => { if (_filtering) { Rebuild(); } };
        Filter.CloseClicked += CloseFilter;

        Header.TagClicked += CycleDimension;
        Header.SortClicked += ToggleSort;
        Artists.SelectionChanged += row => Show(row.Data as LibrarySelection);
        SetCatalog(null, _emptyMessage);
    }

    public DimensionHeader Header { get; }

    /// <summary>The left list (its name is historical: it also lists genres and years).</summary>
    public RowList Artists { get; }

    public AlbumsView Albums { get; }

    public LibraryDimension Dimension => _dimension;
    public const int MinLeftWidth = 22;
    private int _leftWidth = 36;
    private bool _descending;

    public SplitterView Splitter { get; }
    private bool _filtering;

    public FilterBar Filter { get; }

    /// <summary>True while the filter field replaces the "All ..." row.</summary>
    public bool Filtering => _filtering;

    /// <summary>Replaces the "All ..." row with a text field that filters the tags of the left list.</summary>
    public void BeginFilter()
    {
        if (_filtering || _catalog is null)
        {
            return;
        }

        _filtering = true;
        Filter.Field.Text = "";
        Filter.Visible = true;
        SetNeedsLayout();
        Rebuild();
        Filter.Field.SetFocus();
    }

    /// <summary>Removes the filter and restores the "All ..." row.</summary>
    public void CloseFilter()
    {
        if (!_filtering)
        {
            return;
        }

        _filtering = false;
        Filter.Visible = false;
        Filter.Field.Text = "";
        SetNeedsLayout();
        Rebuild();
        Artists.SetFocus();
    }

    private List<Row> ApplyFilter(List<Row> rows)
    {
        if (!_filtering)
        {
            return rows;
        }

        var text = Filter.Field.Text.Trim();
        var kept = rows
            .Where(r => r.Data is LibrarySelection { Key: not null })
            .Where(r => text.Length == 0 || System.Globalization.CultureInfo.InvariantCulture.CompareInfo
                .IndexOf(r.Text, text, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0)
            .ToList();
        if (kept.Count == 0)
        {
            kept.Add(new Row("No match.", Kind: RowKind.More));
        }

        return kept;
    }


    /// <summary>Width of the left column: 22 characters at least, half of the tab at most.</summary>
    public int LeftWidth => Math.Clamp(_leftWidth, MinLeftWidth, Math.Max(MinLeftWidth, Viewport.Width / 2));

    /// <summary>True when the left list is sorted Z to A (or newest year first).</summary>
    public bool Descending => _descending;

    /// <summary>Sets the left column width (clamped) and lays the tab out again.</summary>
    public void SetLeftWidth(int width)
    {
        var before = LeftWidth;
        _leftWidth = Math.Clamp(width, MinLeftWidth, Math.Max(MinLeftWidth, Viewport.Width / 2));
        if (LeftWidth != before)
        {
            Albums.Relayout();
            SetNeedsLayout();
            SetNeedsDraw();
        }
    }

    /// <summary>Flips ascending/descending for the left list ("All ..." stays first).</summary>
    public void ToggleSort()
    {
        _descending = !_descending;
        Rebuild();
    }


    /// <summary>Shows a catalogue (or, when null or empty, the message). Keeps the selection if it still exists.</summary>
    public void SetCatalog(MusicCatalog? catalog, string emptyMessage)
    {
        _catalog = catalog;
        _emptyMessage = emptyMessage;
        Rebuild();
    }

    /// <summary>Switches to the next dimension (Album Artist, Genre, Year).</summary>
    public void CycleDimension()
    {
        _dimension = (LibraryDimension)(((int)_dimension + 1) % 3);
        Rebuild();
    }

    /// <summary>Selects an artist in the Album Artist dimension (from a search result).</summary>
    public void SelectArtist(int artistId)
    {
        _dimension = LibraryDimension.AlbumArtist;
        Rebuild(wantedKey: artistId);
    }

    /// <summary>Selects a genre in the Genre dimension (from a search result).</summary>
    public void SelectGenre(string genre)
    {
        _dimension = LibraryDimension.Genre;
        Rebuild(wantedKey: genre);
    }

    /// <summary>Selects a year in the Year dimension (from a click on an album's year).</summary>
    public void SelectYear(int year)
    {
        _dimension = LibraryDimension.Year;
        Rebuild(wantedKey: year);
    }

    private void Rebuild(object? wantedKey = null)
    {
        Header.DimensionName = NameOf(_dimension);
        Header.Descending = _descending;
        Header.SetNeedsDraw();

        if (_catalog is null || _catalog.Albums.Count == 0)
        {
            Artists.Rows = Array.Empty<Row>();
            Albums.SetMessage(_emptyMessage);
            return;
        }

        // Keep the selection across a catalogue refresh, but not across a dimension switch (keys mean different things).
        var previous = wantedKey;
        if (previous is null && Artists.SelectedRow?.Data is LibrarySelection old && old.Dimension == _dimension)
        {
            previous = old.Key;
        }

        var rows = _dimension switch
        {
            LibraryDimension.Genre => GenreRows(_catalog),
            LibraryDimension.Year => YearRows(_catalog),
            _ => ArtistRows(_catalog),
        };
        if (_descending && rows.Count > 2 && rows[0].Data is LibrarySelection { Key: null })
        {
            rows.Reverse(1, rows.Count - 1);
        }

        rows = ApplyFilter(rows);
        Artists.Rows = rows;

        var index = previous is null ? -1 : FindRow(rows, previous);
        if (index >= 0)
        {
            Artists.SelectIndex(index);
        }
        else
        {
            Show(Artists.SelectedRow?.Data as LibrarySelection);
        }
    }

    private static int FindRow(IReadOnlyList<Row> rows, object key)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Data is LibrarySelection s && Equals(s.Key, key))
            {
                return i;
            }
        }

        return -1;
    }

    private static List<Row> ArtistRows(MusicCatalog c)
    {
        var rows = new List<Row> { new("All Artists", c.Albums.Count.ToString(), Data: new LibrarySelection(LibraryDimension.AlbumArtist, null, "All Artists")) };
        // LMS's "artists" also returns people who only appear on tracks (guests, composers); this dimension is the
        // Album Artist list, so artists without an album of their own are left out.
        rows.AddRange(c.Artists
            .Select(a => (Artist: a, Albums: c.AlbumCountOf(a.Id)))
            .Where(x => x.Albums > 0)
            .Select(x => new Row(x.Artist.Name, x.Albums.ToString(), Data: new LibrarySelection(LibraryDimension.AlbumArtist, x.Artist.Id, x.Artist.Name))));
        return rows;
    }

    private static List<Row> GenreRows(MusicCatalog c)
    {
        var counted = c.Genres.Select(g => (Genre: g, Count: c.AlbumCountOfGenre(g.Name))).Where(x => x.Count > 0).ToList();
        if (counted.Count == 0)
        {
            return new List<Row>
            {
                new("Genres need track data.", Kind: RowKind.Header),
                new(c.TracksLoaded ? "(none found)" : "(sync is still loading tracks)", Kind: RowKind.More),
            };
        }

        var rows = new List<Row> { new("All Genres", c.Albums.Count.ToString(), Data: new LibrarySelection(LibraryDimension.Genre, null, "All Genres")) };
        rows.AddRange(counted.Select(x => new Row(x.Genre.Name, x.Count.ToString(), Data: new LibrarySelection(LibraryDimension.Genre, x.Genre.Name, x.Genre.Name))));
        return rows;
    }

    private static List<Row> YearRows(MusicCatalog c)
    {
        var rows = new List<Row> { new("All Years", c.Albums.Count.ToString(), Data: new LibrarySelection(LibraryDimension.Year, null, "All Years")) };
        rows.AddRange(c.Years.Select(y =>
        {
            var name = y == 0 ? "Unknown year" : y.ToString();
            return new Row(name, c.AlbumsOfYear(y).Count.ToString(), Data: new LibrarySelection(LibraryDimension.Year, y, name));
        }));
        return rows;
    }

    private void Show(LibrarySelection? selection)
    {
        if (_catalog is not { } c || selection is null)
        {
            return;
        }

        IReadOnlyList<CatalogAlbum> albums = (selection.Dimension, selection.Key) switch
        {
            (LibraryDimension.AlbumArtist, int artistId) => c.AlbumsOfArtist(artistId),
            (LibraryDimension.Genre, string genre) => c.AlbumsOfGenre(genre),
            (LibraryDimension.Year, int year) => c.AlbumsOfYear(year),
            _ => c.Albums,
        };

        // Only a single artist's list can leave the artist out; every other list mixes artists.
        Albums.ShowArtist = !(selection.Dimension == LibraryDimension.AlbumArtist && selection.Key is not null);
        Albums.SetAlbums(selection.Heading, albums, c);
    }

    private static string NameOf(LibraryDimension d) => d switch
    {
        LibraryDimension.Genre => "Genre",
        LibraryDimension.Year => "Year",
        _ => "Album Artist",
    };

    /// <summary>
    /// One row above the list: [≡] tag selector (click or F6: Album Artist, Genre, Year), the tag name, and the sort
    /// arrow (click: ascending ▾ / descending ▴).
    /// </summary>
    public sealed class DimensionHeader : PaintView
    {
        private const int IconX = 1;

        public string DimensionName { get; set; } = "Album Artist";

        public bool Descending { get; set; }

        public event Action? TagClicked;

        public event Action? SortClicked;

        private int ArrowX => IconX + 2 + DimensionName.Length + 1;

        protected override Attribute Base => new(Theme.Fg, Theme.BgAlt);

        protected override void Paint()
        {
            FillRow(0, 0, W, Base);
            Put(IconX, 0, "≡", new Attribute(Theme.Fg, Theme.BgSelectedDim));
            Put(IconX + 2, 0, DimensionName, new Attribute(Theme.Accent, Theme.BgAlt));
            Put(ArrowX, 0, Descending ? "▴" : "▾", new Attribute(Theme.Accent, Theme.BgAlt));
            var hint = "F6";
            Put(W - hint.Length - 2, 0, hint, new Attribute(Theme.FgDim, Theme.BgAlt));
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && mouse.Position is { } p)
            {
                if (p.X <= IconX + 1)
                {
                    TagClicked?.Invoke();
                    return true;
                }

                if (p.X >= ArrowX - 1 && p.X <= ArrowX + 1)
                {
                    SortClicked?.Invoke();
                    return true;
                }

                return true;
            }

            return base.OnMouseEvent(mouse);
        }
    }

    /// <summary>The filter strip: a magnifier, the text field and an x that closes it.</summary>
    public sealed class FilterBar : View
    {
        public FilterBar()
        {
            CanFocus = true;
            Field = new TextField { X = 3, Y = 0, Width = Dim.Fill(3), Height = 1 };
            var icon = new Glyph("⌕") { X = 1, Y = 0, Width = 1, Height = 1 };
            var close = new Glyph("x") { X = Pos.AnchorEnd(2), Y = 0, Width = 1, Height = 1 };
            close.Clicked += () => CloseClicked?.Invoke();
            Add(icon, Field, close);
        }

        public TextField Field { get; }

        public event Action? CloseClicked;

        private sealed class Glyph : PaintView
        {
            private readonly string _text;

            public Glyph(string text) => _text = text;

            public event Action? Clicked;

            protected override void Paint() => Put(0, 0, _text, new Attribute(Theme.Accent, Theme.Bg));

            protected override bool OnMouseEvent(Mouse mouse)
            {
                if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
                {
                    Clicked?.Invoke();
                    return true;
                }

                return base.OnMouseEvent(mouse);
            }
        }
    }

    /// <summary>The column between the two panes: drag it sideways to resize the left column.</summary>
    public sealed class SplitterView : PaintView
    {
        private readonly LibraryTab _tab;
        private bool _dragging;

        public SplitterView(LibraryTab tab) => _tab = tab;

        protected override void Paint()
        {
            var attr = new Attribute(_dragging ? Theme.Accent : Theme.Rgb(52, 57, 70), Theme.Bg);
            for (var y = 0; y < H; y++)
            {
                Put(0, y, "│", attr);
            }
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            // Only a press that starts here: a drag begun in another view (a scrollbar) and carried over the handle also
            // reports "pressed", but with PositionReport, and must not take it over.
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) && !mouse.Flags.HasFlag(MouseFlags.PositionReport) && !_dragging)
            {
                _dragging = true;
                App?.Mouse.GrabMouse(this);
                SetNeedsDraw();
                return true;
            }

            if (_dragging)
            {
                // Apply the position first: some terminals only report where the button was released.
                var origin = _tab.ViewportToScreen(new System.Drawing.Point(0, 0)).X;
                _tab.SetLeftWidth(mouse.ScreenPosition.X - origin);
                if (mouse.Flags.HasFlag(MouseFlags.LeftButtonReleased) || mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
                {
                    _dragging = false;
                    App?.Mouse.UngrabMouse();
                    SetNeedsDraw();
                }

                return true;
            }

            return base.OnMouseEvent(mouse);
        }
    }
}
