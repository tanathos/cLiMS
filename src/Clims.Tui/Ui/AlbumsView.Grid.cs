using Clims.Catalog;
using Clims.Tui.Settings;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>
/// The grid layout of <see cref="AlbumsView"/>: covers in rows with title and artist under each; a click on a cover opens a
/// panel (cover, title, tracklist) under its row, and another click closes it. Only one panel is open at a time.
/// </summary>
public sealed partial class AlbumsView
{
    /// <summary>Rows of text under a cover: the title and the artist.</summary>
    private const int ItemTextRows = 2;

    private enum GridPart
    {
        None,
        Cover,
        Title,
        Artist,
        PanelCover,
        Close,
        Play,
        Track,
    }

    private readonly record struct GridTarget(int Album, GridPart Part, int Track = -1);

    private int _expanded = -1;
    private int _gridCols = 1;
    private int _slotW = 1;
    private int _layoutW = -1;
    private int[] _rowTops = Array.Empty<int>();

    // Where the backdrop colours come from while a panel is painted (see PutBack).
    private Func<int, int, Color>? _backdrop;
    private int _backdropTop;

    private static bool Grid => CoverArt.Layout == AlbumLayout.Grid;

    /// <summary>Rows of one grid row without a panel: the cover, the two text rows and a blank one.</summary>
    private static int ItemHeight => CoverRows + ItemTextRows + 1;

    private int PanelContent(CatalogAlbum a) => Math.Max(CoverRows, 3 + Math.Max(1, LayoutOf(a).Rows));

    /// <summary>The panel: a line, a blank row, the content, a blank row and a line.</summary>
    private int PanelHeight(CatalogAlbum a) => PanelContent(a) + 4;

    private int RowOf(int album) => album / _gridCols;

    private int GridRowHeight(int row) => ItemHeight + (_expanded >= 0 && RowOf(_expanded) == row ? PanelHeight(_albums[_expanded]) : 0);

    private int ItemX(int col) => col * _slotW + (_slotW - CoverCols) / 2;

    private static int PanelCoverX => 2;

    private static int PanelTextX => PanelCoverX + CoverCols + 3;

    private int PanelTextW => Math.Max(1, W - 1 - PanelTextX - 4);

    private bool PanelOpenFor(int album) => _expanded == album;

    /// <summary>True when the open panel owns the arrow keys (its album is the selected one).</summary>
    private bool TracksActive => _expanded >= 0 && _expanded == _selAlbum;

    /// <summary>Back to a list or a new grid (the layout was switched): nothing is open, the selected album is shown.</summary>
    public void LayoutChanged()
    {
        _expanded = -1;
        _selTrack = 0;
        _lastVisible = Array.Empty<int>();
        ResetCovers();
        Relayout();
        if (_albums.Count > 0)
        {
            _offset = Math.Clamp(_tops[Math.Min(_selAlbum, _albums.Count - 1)] - HeadingRows, 0, Math.Max(0, _totalRows - H));
        }

        SetNeedsDraw();
    }

    private void GridRelayout()
    {
        _layoutW = W;
        var width = Math.Max(1, W - 1);
        _gridCols = Math.Max(1, (width - 2) / (CoverCols + 4));
        _slotW = Math.Max(1, width / _gridCols);
        if (_expanded >= _albums.Count)
        {
            _expanded = -1;
        }

        var rows = (_albums.Count + _gridCols - 1) / _gridCols;
        _rowTops = new int[rows];
        _tops = new int[_albums.Count];
        var y = HeadingRows;
        for (var r = 0; r < rows; r++)
        {
            _rowTops[r] = y;
            for (var i = r * _gridCols; i < Math.Min(_albums.Count, (r + 1) * _gridCols); i++)
            {
                _tops[i] = y;
            }

            y += GridRowHeight(r);
        }

        _totalRows = y;
        _offset = Math.Clamp(_offset, 0, Math.Max(0, _totalRows - H));
        SetNeedsDraw();
    }

    /// <summary>The grid row that contains the content row, or -1 (between rows, or past the end).</summary>
    private int GridRowAt(int row)
    {
        var lo = 0;
        var hi = _rowTops.Length - 1;
        var found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (_rowTops[mid] <= row)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found >= 0 && row < _rowTops[found] + GridRowHeight(found) ? found : -1;
    }

    private void PaintGrid(List<int> visible)
    {
        if (_layoutW != W)
        {
            GridRelayout();
        }

        var first = Math.Max(0, GridRowAt(_offset) is var r0 and >= 0 ? r0 : 0);
        for (var r = first; r < _rowTops.Length; r++)
        {
            var top = _rowTops[r] - _offset;
            if (top >= H)
            {
                break;
            }

            for (var c = 0; c < _gridCols && r * _gridCols + c < _albums.Count; c++)
            {
                var i = r * _gridCols + c;
                PaintItem(i, top, c);
                visible.Add(_albums[i].Id);
            }

            if (_expanded >= 0 && RowOf(_expanded) == r)
            {
                PaintPanel(top + ItemHeight - 1);
            }
        }
    }

    private void PaintItem(int index, int top, int col)
    {
        var album = _albums[index];
        var x = ItemX(col);
        DrawCover(x, top, CoverCols, CoverRows, new CoverKey(album.ArtworkId, album.Id), CoverPlace.Library);

        var slotX = col * _slotW;
        var room = Math.Max(1, _slotW - 2);
        var selected = index == _selAlbum;
        var open = PanelOpenFor(index);
        var titleAttr = selected ? (HasFocus ? Theme.Selected : Theme.SelectedDim) : new Attribute(open ? Theme.Accent : Theme.Fg, Theme.Bg, TextStyle.Bold);
        var artistAttr = selected ? titleAttr : Theme.Dim;

        var title = Clip(album.Title, room);
        var artist = ShowArtist ? Clip(album.ArtistName, room) : album.Year > 0 ? album.Year.ToString() : "";
        // Every cell of the two text rows is written (a cover that was here a moment ago must not show through) and claimed.
        for (var line = 0; line < ItemTextRows; line++)
        {
            FillRow(top + CoverRows + line, slotX, slotX + _slotW, Theme.Normal);
        }

        Claim(slotX, top + CoverRows, _slotW, ItemTextRows);
        if (selected)
        {
            FillRow(top + CoverRows, slotX + 1, slotX + _slotW - 1, titleAttr);
            FillRow(top + CoverRows + 1, slotX + 1, slotX + _slotW - 1, artistAttr);
        }

        Put(slotX + (_slotW - title.Length) / 2, top + CoverRows, title, titleAttr);
        Put(slotX + (_slotW - artist.Length) / 2, top + CoverRows + 1, artist, artistAttr);
    }

    private void PaintPanel(int top)
    {
        if (_catalog is not { } catalog)
        {
            return;
        }

        var album = _albums[_expanded];
        var tracks = catalog.TracksOf(album.Id);
        var height = PanelHeight(album);
        var key = new CoverKey(album.ArtworkId, album.Id);
        var contentTop = top + 2;

        // Where the open album's own cover is in its row, for the pointer of the panel.
        var pointerX = ItemX(_expanded % _gridCols) + CoverCols / 2;

        _backdrop = AlbumBackdrop.Make(CoverArt.Source.TryGet(key), CoverArt.Backdrop, Math.Max(1, W - 1), height);
        Claim(0, top, Math.Max(1, W - 1), height);
        _backdropTop = top;
        for (var rel = 0; rel < height; rel++)
        {
            var y = top + rel;
            if (y < 0 || y >= H)
            {
                continue;
            }

            var inCover = rel >= 2 && rel < 2 + CoverRows;
            var x = 0;
            while (x < W - 1)
            {
                if (inCover && x >= PanelCoverX && x < PanelCoverX + CoverCols)
                {
                    x = PanelCoverX + CoverCols;
                    continue;
                }

                var colour = BackAt(x, y);
                var end = x + 1;
                while (end < W - 1 && !(inCover && end >= PanelCoverX && end < PanelCoverX + CoverCols) && BackAt(end, y) == colour)
                {
                    end++;
                }

                FillRow(y, x, end, new Attribute(Theme.Fg, colour));
                x = end;
            }
        }

        DrawCover(PanelCoverX, contentTop, CoverCols, CoverRows, key, CoverPlace.Library);

        PutBack(1, top, new string('─', Math.Max(0, W - 3)), Theme.Border);
        PutBack(1, top + height - 1, new string('─', Math.Max(0, W - 3)), Theme.Border);
        PutBack(pointerX, top, "▲", Theme.Fg);

        var textX = PanelTextX;
        var textW = PanelTextW;
        var title = Clip(album.Title, Math.Max(1, textW - 4));
        PutBack(textX, contentTop, title, Theme.Fg, TextStyle.Bold);
        PutBack(textX + title.Length + 2, contentTop, "▶", Theme.Accent);
        PutBack(W - 4, contentTop, "✕", Theme.FgDim);

        var who = ShowArtist && album.ArtistName.Length > 0 ? album.ArtistName : "";
        if (album.Year > 0)
        {
            who += (who.Length > 0 ? " " : "") + $"({album.Year})";
        }

        PutBack(textX, contentTop + 1, Clip(who, textW), Theme.Accent);

        if (tracks.Count == 0)
        {
            PutBack(textX, contentTop + 3, catalog.TracksLoaded ? "No tracks" : "Loading tracks…", Theme.FgDim);
            _backdrop = null;
            return;
        }

        var layout = new TrackLayout(tracks);
        var colW = Math.Max(10, textW / 2);
        foreach (var (headerRow, disc) in layout.Headers)
        {
            PutBack(textX, contentTop + 3 + headerRow, $"Disc {disc}", Theme.Fg, TextStyle.Italic);
        }

        for (var t = 0; t < tracks.Count; t++)
        {
            var (col, row) = layout.Slots[t];
            var y = contentTop + 3 + row;
            if (y < 0 || y >= H)
            {
                continue;
            }

            var x = textX + col * colW;
            var cellW = colW - 2;
            var track = tracks[t];
            var number = track.Number > 0 ? track.Number.ToString() : string.Empty;
            var duration = Fmt(track.Duration);
            var line = $"{number,2} {Clip(track.Title, cellW - duration.Length - 5)}";
            if (TracksActive && t == _selTrack)
            {
                var attr = HasFocus ? Theme.Selected : Theme.SelectedDim;
                FillRow(y, x, x + cellW, attr);
                Put(x, y, line, attr);
                Put(x + cellW - duration.Length, y, duration, attr);
            }
            else
            {
                PutBack(x, y, line, Theme.Fg);
                PutBack(x + cellW - duration.Length, y, duration, Theme.FgDim);
            }
        }

        _backdrop = null;
    }

    private Color BackAt(int x, int y) => _backdrop?.Invoke(Math.Clamp(x, 0, Math.Max(0, W - 2)), Math.Max(0, y - _backdropTop)) ?? Theme.Bg;

    /// <summary>Text over the panel's backdrop: each run of cells with the same background colour is written with that colour.</summary>
    private void PutBack(int x, int y, string text, Color fg, TextStyle style = TextStyle.None)
    {
        var start = 0;
        while (start < text.Length)
        {
            var colour = BackAt(x + start, y);
            var end = start + 1;
            while (end < text.Length && BackAt(x + end, y) == colour)
            {
                end++;
            }

            Put(x + start, y, text[start..end], new Attribute(fg, colour, style));
            start = end;
        }
    }

    /// <summary>What is at a cell of the grid: which album, and which part of it.</summary>
    private GridTarget GridLocate(int x, int y)
    {
        if (_catalog is null || y < HeadingRows || _rowTops.Length == 0 || x >= W - 1)
        {
            return default;
        }

        var row = y + _offset;
        var r = GridRowAt(row);
        if (r < 0)
        {
            return default;
        }

        var rel = row - _rowTops[r];
        if (rel < CoverRows + ItemTextRows)
        {
            var c = x / _slotW;
            var i = r * _gridCols + c;
            if (c >= _gridCols || i >= _albums.Count)
            {
                return default;
            }

            var cx = ItemX(c);
            if (rel < CoverRows)
            {
                return x >= cx && x < cx + CoverCols ? new GridTarget(i, GridPart.Cover) : default;
            }

            if (rel == CoverRows)
            {
                return new GridTarget(i, GridPart.Title);
            }

            var name = _albums[i].ArtistName;
            var width = Math.Min(name.Length, Math.Max(1, _slotW - 2));
            var start = c * _slotW + (_slotW - width) / 2;
            return ShowArtist && name.Length > 0 && x >= start && x < start + width
                ? new GridTarget(i, GridPart.Artist)
                : new GridTarget(i, GridPart.Title);
        }

        if (_expanded < 0 || RowOf(_expanded) != r)
        {
            return default;
        }

        var album = _albums[_expanded];
        var content = rel - ItemHeight + 1 - 2;
        if (content < 0 || content >= PanelContent(album))
        {
            return default;
        }

        if (x >= PanelCoverX && x < PanelCoverX + CoverCols && content < CoverRows)
        {
            return new GridTarget(_expanded, GridPart.PanelCover);
        }

        var textX = PanelTextX;
        if (content == 0)
        {
            var playX = textX + Math.Min(album.Title.Length, Math.Max(1, PanelTextW - 4)) + 2;
            if (x >= W - 5 && x < W - 2)
            {
                return new GridTarget(_expanded, GridPart.Close);
            }

            if (x >= playX && x < playX + 2)
            {
                return new GridTarget(_expanded, GridPart.Play);
            }
        }

        var line = content - 3;
        var colW = Math.Max(10, PanelTextW / 2);
        var col = (x - textX) / colW;
        if (line >= 0 && x >= textX && col <= 1)
        {
            var t = LayoutOf(album).TrackAt(col, line);
            if (t >= 0)
            {
                return new GridTarget(_expanded, GridPart.Track, t);
            }
        }

        return default;
    }

    private void Select(int album)
    {
        _selAlbum = album;
        _selTrack = 0;
    }

    /// <summary>Opens the panel of an album (closing the one that was open), or closes it when it is the one open.</summary>
    private void ToggleExpanded(int album)
    {
        Select(album);
        _expanded = _expanded == album ? -1 : album;
        Relayout();
        if (_expanded >= 0)
        {
            EnsurePanelVisible();
        }

        _lastVisible = Array.Empty<int>();
        SetNeedsDraw();
    }

    private void EnsurePanelVisible()
    {
        var r = RowOf(_expanded);
        var bottom = _rowTops[r] + GridRowHeight(r);
        if (bottom > _offset + H)
        {
            _offset = Math.Min(bottom - H, _rowTops[r] - HeadingRows);
        }

        if (_rowTops[r] < _offset + HeadingRows)
        {
            _offset = _rowTops[r] - HeadingRows;
        }

        _offset = Math.Clamp(_offset, 0, Math.Max(0, _totalRows - H));
    }

    private void EnsureItemVisible(int album)
    {
        var top = _tops[album];
        if (top < _offset + HeadingRows)
        {
            _offset = top - HeadingRows;
        }
        else if (top + ItemHeight > _offset + H)
        {
            _offset = top + ItemHeight - H;
        }

        _offset = Math.Clamp(_offset, 0, Math.Max(0, _totalRows - H));
    }

    private void GridHit(int x, int y, bool doubleClick)
    {
        var target = GridLocate(x, y);
        if (_catalog is not { } catalog || target.Part == GridPart.None)
        {
            return;
        }

        var album = _albums[target.Album];
        switch (target.Part)
        {
            case GridPart.Cover or GridPart.Title or GridPart.PanelCover:
                ToggleExpanded(target.Album);
                break;
            case GridPart.Artist:
                Select(target.Album);
                SetNeedsDraw();
                ArtistClicked?.Invoke(album);
                break;
            case GridPart.Close:
                ToggleExpanded(target.Album);
                break;
            case GridPart.Play:
                PlayRequested?.Invoke(album, catalog.TracksOf(album.Id), 0);
                break;
            case GridPart.Track:
                _selAlbum = target.Album;
                _selTrack = target.Track;
                SetNeedsDraw();
                if (doubleClick)
                {
                    PlayRequested?.Invoke(album, catalog.TracksOf(album.Id), target.Track);
                }

                break;
        }
    }

    private void GridContext(int x, int y, System.Drawing.Point screen)
    {
        var target = GridLocate(x, y);
        if (_catalog is not { } catalog || target.Part is not (GridPart.Cover or GridPart.PanelCover or GridPart.Track))
        {
            return;
        }

        var album = _albums[target.Album];
        _selAlbum = target.Album;
        _selTrack = target.Part == GridPart.Track ? target.Track : 0;
        SetNeedsDraw();
        ContextRequested?.Invoke(album, catalog.TracksOf(album.Id), target.Part == GridPart.Track ? target.Track : -1, screen);
    }

    /// <summary>
    /// Keys of the grid. Without an open panel the arrows move over the covers, Enter opens the panel and a / n act on the whole
    /// album; with one open they move over its tracks, Enter plays and Esc closes it. Returns false for keys the list handles too.
    /// </summary>
    private bool GridKey(Key key)
    {
        if (_albums.Count == 0 || key.IsCtrl || key.IsAlt)
        {
            return false;
        }

        if (TracksActive)
        {
            return key.KeyCode switch
            {
                KeyCode.Esc => Do(() => ToggleExpanded(_expanded)),
                KeyCode.CursorDown => Do(() => MoveGridTrack(1)),
                KeyCode.CursorUp => Do(() => MoveGridTrack(-1)),
                KeyCode.CursorRight => Do(() => MoveGridTrackColumn(1)),
                KeyCode.CursorLeft => Do(() => MoveGridTrackColumn(-1)),
                _ => false,
            };
        }

        switch (key.KeyCode)
        {
            case KeyCode.CursorRight:
                return Do(() => MoveGridAlbum(1));
            case KeyCode.CursorLeft:
                return Do(() => MoveGridAlbum(-1));
            case KeyCode.CursorDown:
                return Do(() => MoveGridAlbum(_gridCols));
            case KeyCode.CursorUp:
                return Do(() => MoveGridAlbum(-_gridCols));
            case KeyCode.Enter:
                return Do(() => ToggleExpanded(_selAlbum));
        }

        // Nothing open: a / n (and A / N) are about the whole album under the selection.
        var rune = key.AsRune.Value;
        if (rune is 'a' or 'A')
        {
            return Do(() => RaiseQueue(QueueAction.Add, wholeAlbum: true));
        }

        if (rune is 'n' or 'N')
        {
            return Do(() => RaiseQueue(QueueAction.PlayNext, wholeAlbum: true));
        }

        return false;
    }

    private static bool Do(Action action)
    {
        action();
        return true;
    }

    private void MoveGridAlbum(int delta)
    {
        var next = Math.Clamp(_selAlbum + delta, 0, _albums.Count - 1);
        Select(next);
        EnsureItemVisible(next);
        SetNeedsDraw();
    }

    private void MoveGridTrack(int delta)
    {
        _selTrack = Math.Clamp(_selTrack + delta, 0, Math.Max(0, TrackCount(_albums[_selAlbum]) - 1));
        EnsureTrackVisible();
        SetNeedsDraw();
    }

    /// <summary>Left / right: the track in the other column on the same line, when there is one.</summary>
    private void MoveGridTrackColumn(int delta)
    {
        var layout = LayoutOf(_albums[_selAlbum]);
        if (_selTrack < layout.Slots.Length)
        {
            var (col, row) = layout.Slots[_selTrack];
            var t = layout.TrackAt(col + delta, row);
            if (t >= 0)
            {
                _selTrack = t;
                SetNeedsDraw();
            }
        }
    }

    private void EnsureTrackVisible()
    {
        var album = _albums[_selAlbum];
        var slots = LayoutOf(album).Slots;
        var line = _tops[_selAlbum] + ItemHeight - 1 + 2 + 3 + (_selTrack < slots.Length ? slots[_selTrack].Row : 0);
        if (line < _offset + HeadingRows)
        {
            _offset = line - HeadingRows;
        }
        else if (line >= _offset + H - 1)
        {
            _offset = line - H + 2;
        }

        _offset = Math.Clamp(_offset, 0, Math.Max(0, _totalRows - H));
    }
}
