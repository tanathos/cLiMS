using Clims.Catalog;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>
/// Right pane of the Library tab: a heading, then one block per album (cover, title, year · genres, duration,
/// two-column tracklist). Block heights vary with the number of tracks, so tops are precomputed and only blocks that
/// intersect the viewport are drawn. Track lists come from the catalogue and can change underneath (on-view refresh),
/// which is detected through <see cref="MusicCatalog.Version"/>.
/// </summary>
public sealed class AlbumsView : PaintView
{
    private const int HeadingRows = 2;

    /// <summary>Covers never reach into the pinned heading.</summary>
    protected override int ClipTop => HeadingRows;

    private MusicCatalog? _catalog;
    private IReadOnlyList<CatalogAlbum> _albums = Array.Empty<CatalogAlbum>();
    private IReadOnlyList<CatalogAlbum> _source = Array.Empty<CatalogAlbum>();
    private bool _yearDescending = true;
    private int[] _tops = Array.Empty<int>();
    private int _totalRows;
    private string _heading = "";
    private string? _message;
    private int _offset;
    private int _selAlbum;
    private int _selTrack;
    private int _version;
    private int[] _lastVisible = Array.Empty<int>();

    public AlbumsView()
    {
        CanFocus = true;
    }

    /// <summary>Play the track at the index (Enter, or a click on a track).</summary>
    public event Action<CatalogAlbum, IReadOnlyList<CatalogTrack>, int>? PlayRequested;

    /// <summary>Add to the queue or play next (keys <c>a</c> and <c>n</c>): wired to the real queue in Step 5.</summary>
    public event Action<QueueAction, CatalogAlbum, IReadOnlyList<CatalogTrack>, int>? QueueRequested;

    /// <summary>The set of albums on screen changed (scrolling, resizing, a new list): the trigger for on-view refresh.</summary>
    public event Action<IReadOnlyList<CatalogAlbum>>? VisibleAlbumsChanged;

    /// <summary>Shows the artist next to the album's year: for lists that mix artists.</summary>
    public bool ShowArtist { get; set; }

    protected override bool StableSixels => true;

    private static int CoverCols => CoverArt.LibraryCells(CoverArt.LibrarySize).Cols;

    private static int CoverRows => CoverArt.LibraryCells(CoverArt.LibrarySize).Rows;

    private static int TextX => CoverCols + 3;

    private int TextWidth => W - 1 - TextX - 2;

    /// <summary>Albums are ordered by year (newest first at the start), and the heading row shows a clickable sort label. Off for lists whose order comes from the server.</summary>
    public bool Sortable { get; set; } = true;

    /// <summary>True when the newest year comes first (the default).</summary>
    public bool YearDescending => _yearDescending;

    /// <summary>Right click on a cover (track index -1: the whole album) or on a track, with the screen cell of the click.</summary>
    public event Action<CatalogAlbum, IReadOnlyList<CatalogTrack>, int, System.Drawing.Point>? ContextRequested;

    /// <summary>A genre in an album's line was clicked.</summary>
    public event Action<string>? GenreClicked;

    /// <summary>The artist in an album's line was clicked.</summary>
    public event Action<CatalogAlbum>? ArtistClicked;

    /// <summary>The year in an album's line was clicked.</summary>
    public event Action<int>? YearClicked;

    private string SortLabel => _yearDescending ? "Year ▼" : "Year ▲";

    /// <summary>Flips the year order (click on the label at the top right) and goes back to the top.</summary>
    public void ToggleSort()
    {
        _yearDescending = !_yearDescending;
        _albums = Ordered(_source);
        _offset = 0;
        _selAlbum = 0;
        _selTrack = 0;
        _lastVisible = Array.Empty<int>();
        Relayout();
    }

    // Albums without a year always go last. The sort is stable: within a year the given order stays.
    private IReadOnlyList<CatalogAlbum> Ordered(IReadOnlyList<CatalogAlbum> albums)
    {
        if (!Sortable)
        {
            return albums;
        }

        return (_yearDescending
            ? albums.OrderByDescending(a => a.Year == 0 ? -1 : a.Year)
            : albums.OrderBy(a => a.Year == 0 ? int.MaxValue : a.Year)).ToList();
    }

    public void SetAlbums(string heading, IReadOnlyList<CatalogAlbum> albums, MusicCatalog catalog)
    {
        // The same list again (a catalogue refresh, or the first build finishing): keep scroll position and selection.
        var same = _message is null && heading == _heading && albums.Count == _source.Count
                   && albums.Zip(_source).All(p => p.First.Id == p.Second.Id);
        _heading = heading;
        _message = null;
        _source = albums;
        _albums = Ordered(albums);
        _catalog = catalog;
        _version = catalog.Version;
        if (!same)
        {
            _offset = 0;
            _selAlbum = 0;
            _selTrack = 0;
        }

        _lastVisible = Array.Empty<int>();
        Relayout();
        _selAlbum = Math.Min(_selAlbum, Math.Max(0, _albums.Count - 1));
        _selTrack = Math.Min(_selTrack, Math.Max(0, _albums.Count == 0 ? 0 : TrackCount(_albums[_selAlbum]) - 1));
    }

    /// <summary>Scrolls the album to the top of the view and selects its first track. Returns false when it is not listed.</summary>
    public bool ScrollToAlbum(int albumId)
    {
        for (var i = 0; i < _albums.Count; i++)
        {
            if (_albums[i].Id == albumId)
            {
                _selAlbum = i;
                _selTrack = 0;
                Relayout();
                _offset = Math.Clamp(_tops[i] - HeadingRows, 0, Math.Max(0, _totalRows - H));
                SetNeedsDraw();
                return true;
            }
        }

        return false;
    }

    /// <summary>Forget which albums were reported visible, so the next paint reports them again (the tab was hidden).</summary>
    public void ForgetVisible() => _lastVisible = Array.Empty<int>();

    /// <summary>Shows a message instead of albums (nothing synced yet, nothing selected).</summary>
    public void SetMessage(string message)
    {
        _message = message;
        _source = Array.Empty<CatalogAlbum>();
        _albums = Array.Empty<CatalogAlbum>();
        _catalog = null;
        _lastVisible = Array.Empty<int>();
        Relayout();
    }

    /// <summary>Recomputes block positions, for example after the cover size or a track list changed. Keeps the scroll offset.</summary>
    public void Relayout()
    {
        _tops = new int[_albums.Count];
        var y = HeadingRows;
        for (var i = 0; i < _albums.Count; i++)
        {
            _tops[i] = y;
            y += BlockHeight(_albums[i]);
        }

        _totalRows = y;
        _offset = Math.Clamp(_offset, 0, Math.Max(0, _totalRows - H));
        SetNeedsDraw();
    }

    private int TrackCount(CatalogAlbum a) => _catalog?.TracksOf(a.Id).Count ?? 0;
    /// <summary>
    /// Where each track of an album goes: two columns filled top to bottom. An album with several discs gets a "Disc N"
    /// line before each disc, and every disc is split into its own two columns.
    /// </summary>
    public sealed class TrackLayout
    {
        public TrackLayout(IReadOnlyList<CatalogTrack> tracks)
        {
            Slots = new (int Col, int Row)[tracks.Count];
            var discs = tracks.Select(t => t.Disc).Distinct().ToList();
            var row = 0;
            foreach (var disc in discs)
            {
                if (discs.Count > 1)
                {
                    if (row > 0)
                    {
                        row++; // a blank line between discs
                    }

                    Headers.Add((row, disc));
                    row++;
                }

                var indexes = Enumerable.Range(0, tracks.Count).Where(i => tracks[i].Disc == disc).ToList();
                var rows = (indexes.Count + 1) / 2;
                for (var k = 0; k < indexes.Count; k++)
                {
                    Slots[indexes[k]] = (k / rows, row + k % rows);
                }

                row += rows;
            }

            Rows = row;
        }

        /// <summary>Column and row (below the first line of tracks) of each track.</summary>
        public (int Col, int Row)[] Slots { get; }

        /// <summary>Rows with a "Disc N" title.</summary>
        public List<(int Row, int Disc)> Headers { get; } = new();

        public int Rows { get; }

        /// <summary>The track at a column and row, or -1.</summary>
        public int TrackAt(int col, int row)
        {
            for (var i = 0; i < Slots.Length; i++)
            {
                if (Slots[i].Col == col && Slots[i].Row == row)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    private TrackLayout LayoutOf(CatalogAlbum a) => new(_catalog?.TracksOf(a.Id) ?? Array.Empty<CatalogTrack>());

    private int BlockHeight(CatalogAlbum a) => Math.Max(CoverRows, 3 + Math.Max(1, LayoutOf(a).Rows)) + 1;


    protected override void Paint()
    {
        if (_message is not null)
        {
            Put(2, 1, _message, Theme.Dim);
            return;
        }

        if (_catalog is { } catalog && catalog.Version != _version)
        {
            RelayoutKeepingPosition(catalog);
        }

        // A resize can leave the old offset past the new maximum.
        _offset = Math.Clamp(_offset, 0, Math.Max(0, _totalRows - H));

        var visible = new List<int>();
        for (var i = FirstVisible(); i < _albums.Count; i++)
        {
            var top = _tops[i] - _offset;
            if (top >= H)
            {
                break;
            }

            PaintAlbum(_albums[i], i, top);
            visible.Add(_albums[i].Id);
        }

        // The heading stays where it is: drawn last, over whatever scrolled up under it.
        for (var y = 0; y < HeadingRows; y++)
        {
            FillRow(y, 0, W, Theme.Normal);
        }

        var headingW = W - 3;
        if (Sortable)
        {
            var label = SortLabel;
            headingW = W - 2 - label.Length - 3;
            Put(W - 2 - label.Length, 0, label, new Attribute(Theme.Accent, Theme.Bg));
        }

        Put(1, 0, Clip(_heading, Math.Max(1, headingW)), new Attribute(Theme.Accent, Theme.Bg));
        Put(1, 1, new string('─', Math.Max(0, W - 3)), Theme.Dim);

        PaintScrollbar(_totalRows, _offset, H);

        if (!visible.SequenceEqual(_lastVisible))
        {
            _lastVisible = visible.ToArray();
            VisibleAlbumsChanged?.Invoke(visible.Select(id => _catalog!.Album(id)!).ToList());
        }
    }

    /// <summary>Track lists changed: keep the album that was at the top of the screen at the same spot.</summary>
    private void RelayoutKeepingPosition(MusicCatalog catalog)
    {
        _version = catalog.Version;
        if (_albums.Count == 0)
        {
            return;
        }

        var anchor = FirstVisible();
        var delta = _offset - _tops[anchor];
        Relayout();
        _offset = Math.Clamp(_tops[anchor] + delta, 0, Math.Max(0, _totalRows - H));
        _selTrack = Math.Min(_selTrack, Math.Max(0, TrackCount(_albums[_selAlbum]) - 1));
    }

    private int FirstVisible()
    {
        var lo = 0;
        var hi = _albums.Count - 1;
        var found = 0;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (_tops[mid] + BlockHeight(_albums[mid]) > _offset)
            {
                found = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        return found;
    }

    private void PaintAlbum(CatalogAlbum album, int index, int top)
    {
        var catalog = _catalog!;
        var tracks = catalog.TracksOf(album.Id);
        var summary = catalog.Summary(album.Id);
        DrawCover(1, top, CoverCols, CoverRows, new CoverKey(album.ArtworkId, album.Id), CoverPlace.Library);

        var duration = summary.TrackCount > 0 ? $"{(int)summary.Duration.TotalMinutes} min" : "";
        var textW = TextWidth;
        Put(W - 1 - duration.Length - 2, top, duration, Theme.Dim);
        Put(TextX, top, Clip(album.Title, textW - duration.Length - 1), new Attribute(Theme.Fg, Theme.Bg));

        var (full, parts) = Details(album, summary.Genres);
        Put(TextX, top + 1, Clip(full, textW), Theme.Dim);

        // Genres in italic: redraw each genre over the line.
        foreach (var part in parts)
        {
            if (part.X < textW && (part.Genre is not null || part.Year > 0 || part.Artist))
            {
                // Links: underlined (genres also italic).
                var style = part.Genre is not null ? TextStyle.Italic | TextStyle.Underline : TextStyle.Underline;
                var hovered = _hoverLink == (index, part.X);
                Put(TextX + part.X, top + 1, Clip(part.Text, textW - part.X), new Attribute(hovered ? Theme.HoverFg : Theme.FgDim, Theme.Bg, style));
            }
        }

        if (tracks.Count == 0)
        {
            Put(TextX, top + 3, catalog.TracksLoaded ? "No tracks" : "Loading tracks…", Theme.Dim);
            return;
        }

        var layout = new TrackLayout(tracks);
        var colW = Math.Max(10, textW / 2);
        foreach (var (headerRow, disc) in layout.Headers)
        {
            Put(TextX, top + 3 + headerRow, $"Disc {disc}", new Attribute(Theme.Fg, Theme.Bg, TextStyle.Italic));
        }

        for (var t = 0; t < tracks.Count; t++)
        {
            var (col, row) = layout.Slots[t];
            var y = top + 3 + row;
            if (y < 0 || y >= H)
            {
                continue;
            }

            var x = TextX + col * colW;
            var track = tracks[t];
            var selected = index == _selAlbum && t == _selTrack;
            var attr = selected ? (HasFocus ? Theme.Selected : Theme.SelectedDim) : Theme.Normal;
            var dur = Fmt(track.Duration);
            var cellW = colW - 2;
            var number = track.Number > 0 ? track.Number.ToString() : string.Empty;
            FillRow(y, x, x + cellW, attr);
            Put(x, y, $"{number,2} {Clip(track.Title, cellW - dur.Length - 5)}", attr);
            Put(x + cellW - dur.Length, y, dur, selected ? attr : new Attribute(Theme.FgDim, Theme.Bg));
        }
    }

    /// <summary>One piece of the "artist · year · genre; genre" line: where it starts and what a click on it means.</summary>
    private readonly record struct Detail(int X, string Text, string? Genre, int Year, bool Artist = false);

    private (string Line, List<Detail> Parts) Details(CatalogAlbum album, IEnumerable<string> genres)
    {
        var text = new System.Text.StringBuilder();
        var parts = new List<Detail>();
        void Add(string piece, string? genre, int year, string separator, bool artist = false)
        {
            if (text.Length > 0)
            {
                text.Append(separator);
            }

            parts.Add(new Detail(text.Length, piece, genre, year, artist));
            text.Append(piece);
        }

        if (ShowArtist && album.ArtistName.Length > 0)
        {
            Add(album.ArtistName, null, 0, " · ", artist: true);
        }

        if (album.Year > 0)
        {
            Add(album.Year.ToString(), null, album.Year, " · ");
        }

        var first = true;
        foreach (var genre in genres)
        {
            Add(genre, genre, 0, first ? " · " : "; ");
            first = false;
        }

        return (text.ToString(), parts);
    }

    protected override bool OnKeyDown(Key key)
    {
        switch (key.KeyCode)
        {
            case KeyCode.CursorDown:
                MoveTrack(1);
                return true;
            case KeyCode.CursorUp:
                MoveTrack(-1);
                return true;
            case KeyCode.PageDown:
                Scroll(Math.Max(1, H - 2));
                return true;
            case KeyCode.PageUp:
                Scroll(-Math.Max(1, H - 2));
                return true;
            case KeyCode.Home:
                _selAlbum = 0;
                _selTrack = 0;
                _offset = 0;
                SetNeedsDraw();
                return true;
            case KeyCode.Enter:
                RaisePlay();
                return true;
        }

        // Lower case acts on the highlighted track, upper case on the whole album.
        if (!key.IsCtrl && !key.IsAlt && key.AsRune.Value is 'a' or 'A')
        {
            RaiseQueue(QueueAction.Add, wholeAlbum: key.AsRune.Value == 'A');
            return true;
        }

        if (!key.IsCtrl && !key.IsAlt && key.AsRune.Value is 'n' or 'N')
        {
            RaiseQueue(QueueAction.PlayNext, wholeAlbum: key.AsRune.Value == 'N');
            return true;
        }

        return base.OnKeyDown(key);
    }

    private void RaisePlay()
    {
        if (_albums.Count > 0 && _catalog is { } catalog)
        {
            var album = _albums[_selAlbum];
            PlayRequested?.Invoke(album, catalog.TracksOf(album.Id), _selTrack);
        }
    }

    private void RaiseQueue(QueueAction action, bool wholeAlbum)
    {
        if (_albums.Count > 0 && _catalog is { } catalog)
        {
            var album = _albums[_selAlbum];
            var tracks = catalog.TracksOf(album.Id);
            var chosen = wholeAlbum || _selTrack >= tracks.Count ? tracks : new[] { tracks[_selTrack] };
            QueueRequested?.Invoke(action, album, chosen, wholeAlbum ? 0 : _selTrack);
        }
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (StrayClick(mouse))
        {
            return true;
        }

        // Wheel works on the hovered pane whether or not it has focus.
        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            Scroll(3);
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            Scroll(-3);
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.RightButtonClicked) && mouse.Position is { } rp)
        {
            SetFocus();
            RaiseContext(rp.X, rp.Y, mouse.ScreenPosition);
            return true;
        }

        if (ScrollbarMouse(mouse, _totalRows > H, y =>
            {
                _offset = OffsetFromScrollbar(y, _totalRows, H);
                SetNeedsDraw();
            }))
        {
            return true;
        }

        var click = mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) || mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked);
        var doubleClick = mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked);
        if (click && mouse.Position is { } p)
        {
            if (click)
            {
                SetFocus();
                HitTest(p.X, p.Y, doubleClick);
                return true;
            }
        }

        return base.OnMouseEvent(mouse);
    }

    /// <summary>
    /// Right click on a cover (the whole album) or on a track (just that track): selects it and asks for a menu.
    /// The track index is -1 for the whole album.
    /// </summary>
    private void RaiseContext(int x, int y, System.Drawing.Point screen)
    {
        if (_catalog is not { } catalog)
        {
            return;
        }

        if (y < HeadingRows)
        {
            return; // the pinned heading
        }

        var row = y + _offset;
        for (var i = FirstVisible(); i < _albums.Count && _tops[i] <= row; i++)
        {
            var album = _albums[i];
            var tracks = catalog.TracksOf(album.Id);
            if (row < _tops[i] + CoverRows && x >= 1 && x < 1 + CoverCols)
            {
                _selAlbum = i;
                _selTrack = 0;
                SetNeedsDraw();
                ContextRequested?.Invoke(album, tracks, -1, screen);
                return;
            }

            var line = row - _tops[i] - 3;
            var colW = Math.Max(10, TextWidth / 2);
            var col = (x - TextX) / colW;
            if (line < 0 || x < TextX || col > 1)
            {
                continue;
            }

            var t = new TrackLayout(tracks).TrackAt(col, line);
            if (t >= 0)
            {
                _selAlbum = i;
                _selTrack = t;
                SetNeedsDraw();
                ContextRequested?.Invoke(album, tracks, t, screen);
                return;
            }
        }
    }

    private (int Album, int X)? _hoverLink;

    protected override bool TracksHover => true;

    /// <summary>Only a change of the link under the pointer repaints: the covers must not be redrawn for every cell it crosses.</summary>
    protected override void OnPointerMoved()
    {
        var link = Pointer is { } p ? LinkAt(p.X, p.Y) : null;
        if (link != _hoverLink)
        {
            _hoverLink = link;
            SetNeedsDraw();
        }
    }

    /// <summary>The album and the column (within the details line) of the link (artist, genre, year) at this cell, if any.</summary>
    private (int Album, int X)? LinkAt(int x, int y)
    {
        if (_catalog is not { } catalog)
        {
            return null;
        }

        if (y < HeadingRows)
        {
            return null;
        }

        var row = y + _offset;
        for (var i = FirstVisible(); i < _albums.Count && _tops[i] <= row; i++)
        {
            if (row != _tops[i] + 1)
            {
                continue;
            }

            var (_, parts) = Details(_albums[i], catalog.Summary(_albums[i].Id).Genres);
            foreach (var part in parts)
            {
                var start = TextX + part.X;
                if ((part.Genre is not null || part.Year > 0 || part.Artist) && x >= start && x < Math.Min(start + part.Text.Length, TextX + TextWidth))
                {
                    return (i, part.X);
                }
            }
        }

        return null;
    }

    private void HitTest(int x, int y, bool play)
    {
        if (_catalog is not { } catalog)
        {
            return;
        }

        if (y < HeadingRows)
        {
            if (Sortable && y == 0 && x >= W - 2 - SortLabel.Length - 1)
            {
                ToggleSort();
            }

            return;
        }

        var row = y + _offset;

        for (var i = FirstVisible(); i < _albums.Count && _tops[i] <= row; i++)
        {
            var album = _albums[i];
            if (row == _tops[i] + 1)
            {
                var (_, parts) = Details(album, catalog.Summary(album.Id).Genres);
                foreach (var part in parts)
                {
                    var start = TextX + part.X;
                    if (x >= start && x < Math.Min(start + part.Text.Length, TextX + TextWidth))
                    {
                        if (part.Artist)
                        {
                            ArtistClicked?.Invoke(album);
                        }
                        else if (part.Genre is not null)
                        {
                            GenreClicked?.Invoke(part.Genre);
                        }
                        else if (part.Year > 0)
                        {
                            YearClicked?.Invoke(part.Year);
                        }

                        return;
                    }
                }

                continue;
            }

            var tracks = catalog.TracksOf(album.Id);
            var line = row - _tops[i] - 3;
            var colW = Math.Max(10, TextWidth / 2);
            var col = (x - TextX) / colW;
            if (line < 0 || x < TextX || col > 1)
            {
                continue;
            }

            var t = new TrackLayout(tracks).TrackAt(col, line);
            if (t >= 0)
            {
                _selAlbum = i;
                _selTrack = t;
                SetNeedsDraw();
                if (play)
                {
                    PlayRequested?.Invoke(album, tracks, t);
                }
            }
        }
    }

    private void MoveTrack(int delta)
    {
        if (_albums.Count == 0)
        {
            return;
        }

        _selTrack += delta;
        var count = TrackCount(_albums[_selAlbum]);
        if (_selTrack >= count)
        {
            if (_selAlbum < _albums.Count - 1)
            {
                _selAlbum++;
                _selTrack = 0;
            }
            else
            {
                _selTrack = Math.Max(0, count - 1);
            }
        }
        else if (_selTrack < 0)
        {
            if (_selAlbum > 0)
            {
                _selAlbum--;
                _selTrack = Math.Max(0, TrackCount(_albums[_selAlbum]) - 1);
            }
            else
            {
                _selTrack = 0;
            }
        }

        EnsureSelectionVisible();
        SetNeedsDraw();
    }

    private void EnsureSelectionVisible()
    {
        var slots = LayoutOf(_albums[_selAlbum]).Slots;
        var line = _tops[_selAlbum] + 3 + (_selTrack < slots.Length ? slots[_selTrack].Row : 0);
        if (line < _offset + HeadingRows)
        {
            _offset = Math.Max(0, _tops[_selAlbum] - HeadingRows);
        }
        else if (line >= _offset + H)
        {
            _offset = line - H + 2;
        }

        _offset = Math.Clamp(_offset, 0, Math.Max(0, _totalRows - H));
    }

    private void Scroll(int delta)
    {
        _offset = Math.Clamp(_offset + delta, 0, Math.Max(0, _totalRows - H));
        SetNeedsDraw();
    }
}
