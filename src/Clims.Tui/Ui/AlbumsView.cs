using Clims.Catalog;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>What the user asked to do with a track or album besides playing it right now.</summary>
public enum QueueAction
{
    /// <summary>Append to the end of the queue.</summary>
    Add,

    /// <summary>Insert right after the current track.</summary>
    PlayNext,
}

/// <summary>
/// Right pane of the Library tab: a heading, then one block per album (cover, title, year · genres, duration,
/// two-column tracklist). Block heights vary with the number of tracks, so tops are precomputed and only blocks that
/// intersect the viewport are drawn. Track lists come from the catalogue and can change underneath (on-view refresh),
/// which is detected through <see cref="MusicCatalog.Version"/>.
/// </summary>
public sealed class AlbumsView : PaintView
{
    private const int HeadingRows = 2;

    private MusicCatalog? _catalog;
    private IReadOnlyList<CatalogAlbum> _albums = Array.Empty<CatalogAlbum>();
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

    private static int CoverCols => CoverArt.Large ? 24 : 16;

    private static int CoverRows => CoverArt.Large ? 12 : 8;

    private static int TextX => CoverCols + 3;

    private int TextWidth => W - 1 - TextX - 2;

    public void SetAlbums(string heading, IReadOnlyList<CatalogAlbum> albums, MusicCatalog catalog)
    {
        // The same list again (a catalogue refresh, or the first build finishing): keep scroll position and selection.
        var same = _message is null && heading == _heading && albums.Count == _albums.Count
                   && albums.Zip(_albums).All(p => p.First.Id == p.Second.Id);
        _heading = heading;
        _message = null;
        _albums = albums;
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
                _offset = Math.Clamp(_tops[i], 0, Math.Max(0, _totalRows - H));
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

    private static int TrackRows(int trackCount) => (trackCount + 1) / 2;

    private int BlockHeight(CatalogAlbum a) => Math.Max(CoverRows, 3 + Math.Max(1, TrackRows(TrackCount(a)))) + 1;

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

        Put(1, -_offset, _heading, new Attribute(Theme.Accent, Theme.Bg));
        Put(1, 1 - _offset, new string('─', Math.Max(0, W - 3)), Theme.Dim);

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
        DrawCover(1, top, CoverCols, CoverRows, new CoverKey(album.ArtworkId, album.Id));

        var duration = summary.TrackCount > 0 ? $"{(int)summary.Duration.TotalMinutes} min" : "";
        var textW = TextWidth;
        Put(W - 1 - duration.Length - 2, top, duration, Theme.Dim);
        Put(TextX, top, Clip(album.Title, textW - duration.Length - 1), new Attribute(Theme.Fg, Theme.Bg));

        var details = new List<string>();
        if (ShowArtist && album.ArtistName.Length > 0)
        {
            details.Add(album.ArtistName);
        }

        if (album.Year > 0)
        {
            details.Add(album.Year.ToString());
        }

        if (summary.Genres.Count > 0)
        {
            details.Add(string.Join("; ", summary.Genres));
        }

        var line = Clip(string.Join(" · ", details), textW);
        Put(TextX, top + 1, line, Theme.Dim);

        // Genres in italic: redraw the genre part of the (already clipped) line over itself.
        if (summary.Genres.Count > 0)
        {
            var genres = string.Join("; ", summary.Genres);
            var at = line.LastIndexOf(genres, StringComparison.Ordinal);
            if (at < 0 && line.Length > 0)
            {
                // Clipped: italicise whatever remains after the last separator.
                var cut = line.LastIndexOf(" · ", StringComparison.Ordinal);
                at = details.Count > 1 && cut >= 0 ? cut + 3 : (details.Count == 1 ? 0 : -1);
            }

            if (at >= 0 && at < line.Length)
            {
                Put(TextX + at, top + 1, line[at..], new Attribute(Theme.FgDim, Theme.Bg, TextStyle.Italic));
            }
        }

        if (tracks.Count == 0)
        {
            Put(TextX, top + 3, catalog.TracksLoaded ? "No tracks" : "Loading tracks…", Theme.Dim);
            return;
        }

        var rows = TrackRows(tracks.Count);
        var colW = Math.Max(10, textW / 2);
        for (var t = 0; t < tracks.Count; t++)
        {
            var col = t / rows;
            var row = t % rows;
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

        var click = mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) || mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked);
        var doubleClick = mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked);
        var press = mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed);
        if ((click || press) && mouse.Position is { } p)
        {
            if (p.X >= W - 1 && _totalRows > H)
            {
                _offset = OffsetFromScrollbar(p.Y, _totalRows, H);
                SetNeedsDraw();
                return true;
            }

            if (click)
            {
                SetFocus();
                HitTest(p.X, p.Y, doubleClick);
                return true;
            }
        }

        return base.OnMouseEvent(mouse);
    }

    private void HitTest(int x, int y, bool play)
    {
        if (_catalog is not { } catalog)
        {
            return;
        }

        var row = y + _offset;
        for (var i = FirstVisible(); i < _albums.Count && _tops[i] <= row; i++)
        {
            var album = _albums[i];
            var tracks = catalog.TracksOf(album.Id);
            var rows = TrackRows(tracks.Count);
            var line = row - _tops[i] - 3;
            var colW = Math.Max(10, TextWidth / 2);
            var col = (x - TextX) / colW;
            if (rows == 0 || line < 0 || line >= rows || x < TextX || col > 1)
            {
                continue;
            }

            var t = col * rows + line;
            if (t < tracks.Count)
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
        var rows = Math.Max(1, TrackRows(TrackCount(_albums[_selAlbum])));
        var line = _tops[_selAlbum] + 3 + _selTrack % rows;
        if (line < _offset)
        {
            _offset = Math.Max(0, _tops[_selAlbum]);
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
