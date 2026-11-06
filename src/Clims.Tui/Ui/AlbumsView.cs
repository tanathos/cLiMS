using Clims.Tui.Data;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>
/// Right pane of the Library tab: artist heading, then one block per album (cover, title, year · genres,
/// duration, two-column tracklist). Block heights vary, so tops are precomputed and only blocks that
/// intersect the viewport are drawn.
/// </summary>
public sealed class AlbumsView : PaintView
{
    private const int HeadingRows = 2;

    private IReadOnlyList<AlbumItem> _albums = Array.Empty<AlbumItem>();
    private int[] _tops = Array.Empty<int>();
    private int _totalRows;
    private string _heading = "";
    private int _offset;
    private int _selAlbum;
    private int _selTrack;

    public AlbumsView()
    {
        CanFocus = true;
    }

    public event Action<AlbumItem, int>? PlayRequested;

    private static int CoverCols => CoverArt.Large ? 24 : 16;

    private static int CoverRows => CoverArt.Large ? 12 : 8;

    private static int TextX => CoverCols + 3;

    public void SetAlbums(string heading, IReadOnlyList<AlbumItem> albums)
    {
        _heading = heading;
        _albums = albums;
        Relayout();
        _offset = 0;
        _selAlbum = 0;
        _selTrack = 0;
    }

    /// <summary>Recomputes block positions, e.g. after the cover size changed. Keeps the scroll offset.</summary>
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

    private static int TrackRows(AlbumItem a) => (a.Tracks.Count + 1) / 2;

    private static int BlockHeight(AlbumItem a) => Math.Max(CoverRows, 3 + TrackRows(a)) + 1;

    protected override void Paint()
    {
        // A resize can leave the old offset past the new maximum.
        _offset = Math.Clamp(_offset, 0, Math.Max(0, _totalRows - H));

        Put(1, -_offset, _heading, new Attribute(Theme.Accent, Theme.Bg));
        Put(1, 1 - _offset, new string('─', Math.Max(0, W - 3)), Theme.Dim);

        var first = FirstVisible();
        for (var i = first; i < _albums.Count; i++)
        {
            var top = _tops[i] - _offset;
            if (top >= H)
            {
                break;
            }

            PaintAlbum(_albums[i], i, top);
        }

        PaintScrollbar(_totalRows, _offset, H);
    }

    /// <summary>Width left for content once the scrollbar column is reserved.</summary>
    private int TextWidth => W - 1 - TextX - 2;

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

    private void PaintAlbum(AlbumItem album, int index, int top)
    {
        DrawCover(1, top, CoverCols, CoverRows, album.ArtSeed);

        var duration = $"{(int)album.Duration.TotalMinutes} min";
        var textW = TextWidth;
        Put(W - 1 - duration.Length - 2, top, duration, Theme.Dim);
        Put(TextX, top, Clip(album.Title, textW - duration.Length - 1), new Attribute(Theme.Fg, Theme.Bg));
        Put(TextX, top + 1, Clip($"{album.Year} · {album.Genres}", textW), Theme.Dim);

        var rows = TrackRows(album);
        var colW = Math.Max(10, textW / 2);
        for (var t = 0; t < album.Tracks.Count; t++)
        {
            var col = t / rows;
            var row = t % rows;
            var y = top + 3 + row;
            if (y < 0 || y >= H)
            {
                continue;
            }

            var x = TextX + col * colW;
            var track = album.Tracks[t];
            var selected = index == _selAlbum && t == _selTrack;
            var attr = selected ? (HasFocus ? Theme.Selected : Theme.SelectedDim) : Theme.Normal;
            var dur = Fmt(track.Duration);
            var cellW = colW - 2;
            FillRow(y, x, x + cellW, attr);
            Put(x, y, $"{track.Number,2} {Clip(track.Title, cellW - dur.Length - 5)}", attr);
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
                if (_albums.Count > 0)
                {
                    PlayRequested?.Invoke(_albums[_selAlbum], _selTrack);
                }

                return true;
        }

        return base.OnKeyDown(key);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
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

        var click = mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked);
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
                HitTest(p.X, p.Y);
                return true;
            }
        }

        return base.OnMouseEvent(mouse);
    }

    private void HitTest(int x, int y)
    {
        var row = y + _offset;
        var first = FirstVisible();
        for (var i = first; i < _albums.Count && _tops[i] <= row; i++)
        {
            var album = _albums[i];
            var rows = TrackRows(album);
            var line = row - _tops[i] - 3;
            var colW = Math.Max(10, TextWidth / 2);
            var col = (x - TextX) / colW;
            if (line < 0 || line >= rows || x < TextX || col > 1)
            {
                continue;
            }

            var t = col * rows + line;
            if (t < album.Tracks.Count)
            {
                _selAlbum = i;
                _selTrack = t;
                SetNeedsDraw();
                PlayRequested?.Invoke(album, t);
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
        if (_selTrack >= _albums[_selAlbum].Tracks.Count)
        {
            if (_selAlbum < _albums.Count - 1)
            {
                _selAlbum++;
                _selTrack = 0;
            }
            else
            {
                _selTrack = _albums[_selAlbum].Tracks.Count - 1;
            }
        }
        else if (_selTrack < 0)
        {
            if (_selAlbum > 0)
            {
                _selAlbum--;
                _selTrack = _albums[_selAlbum].Tracks.Count - 1;
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
        var album = _albums[_selAlbum];
        var rows = TrackRows(album);
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
