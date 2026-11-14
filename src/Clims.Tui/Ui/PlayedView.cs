using System.Globalization;
using Clims.Catalog;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>
/// The Played tab: what was played lately, from the play log. Three lists, switched with Left / Right or a click on their name:
/// the tracks of the last few days (a setting), and the albums listened to (half of them or more) in the last 7 and the last 30 days.
/// The albums are a grid of covers (as in the Library's grid layout) with title, artist and how often the album was listened to; no tracklists.
/// Enter or a double click opens the album in the Library, a right click offers Play / Enqueue. Tab switches the list too (the arrows move over the covers).
/// </summary>
public sealed class PlayedView : PaintView
{
    private const int HeadRows = 2;

    /// <summary>What a list shows.</summary>
    public enum Section
    {
        Tracks,
        Week,
        Month,
    }

    private readonly List<(int X0, int X1, Section Section)> _spans = new();
    private MusicCatalog? _catalog;
    private Dictionary<string, CatalogAlbum> _byName = new();
    private IReadOnlyList<PlayRecord> _tracks = Array.Empty<PlayRecord>();
    private IReadOnlyList<AlbumListen> _week = Array.Empty<AlbumListen>();
    private IReadOnlyList<AlbumListen> _month = Array.Empty<AlbumListen>();
    private DateTimeOffset _now = DateTimeOffset.Now;
    private int _trackDays = 3;
    private int _percent = 50;
    private string? _message;
    private Section _section;
    private int _offset;
    private int _selected;

    public PlayedView()
    {
        CanFocus = true;
    }

    /// <summary>Play a logged track inside its album (Enter, double click on a track).</summary>
    public event Action<CatalogAlbum, IReadOnlyList<CatalogTrack>, int>? PlayRequested;

    /// <summary>Show an album in the Library (Enter, double click on an album).</summary>
    public event Action<CatalogAlbum>? OpenRequested;

    /// <summary>Right click on an album, or on a track (its index in the album, else -1): the menu with Play and Enqueue.</summary>
    public event Action<CatalogAlbum, IReadOnlyList<CatalogTrack>, int, System.Drawing.Point>? ContextRequested;

    protected override bool StableSixels => true;

    protected override int ClipTop => HeadRows;

    private static int CoverCols => CoverArt.LibraryCells(CoverArt.LibrarySize).Cols;

    private static int CoverRows => CoverArt.LibraryCells(CoverArt.LibrarySize).Rows;

    private bool OnTracks => _section == Section.Tracks;

    /// <summary>Rows of text under a cover: the title, the artist and the listens.</summary>
    private const int TextRows = 3;

    /// <summary>Covers across: one list of tracks is a single column.</summary>
    private int Cols => OnTracks ? 1 : Math.Max(1, (W - 1 - 2) / (CoverCols + 4));

    private int SlotW => OnTracks ? Math.Max(1, W - 1) : Math.Max(1, (W - 1) / Cols);

    /// <summary>Rows of one line of the list: a track, or a cover with its text and a blank row.</summary>
    private int ItemHeight => OnTracks ? 1 : CoverRows + TextRows + 1;

    private int TotalRows => (Count + Cols - 1) / Cols * ItemHeight;

    private int Count => _section switch
    {
        Section.Tracks => _tracks.Count,
        Section.Week => _week.Count,
        _ => _month.Count,
    };

    /// <summary>The section on show.</summary>
    public Section Current => _section;

    /// <summary>A message instead of the lists (no play log, no server), or null.</summary>
    public void SetMessage(string? message)
    {
        _message = message;
        SetNeedsDraw();
    }

    /// <summary>Computes the three lists from the log.</summary>
    /// <param name="plays">Every play logged.</param>
    /// <param name="catalog">The index, for covers and for playing (may be null).</param>
    /// <param name="trackDays">How many days back the track list goes.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="listenPercent">How much of an album must be played to count as a listen.</param>
    public void SetData(IReadOnlyList<PlayRecord> plays, MusicCatalog? catalog, int trackDays, DateTimeOffset now, int listenPercent = 50)
    {
        _catalog = catalog;
        _byName = new Dictionary<string, CatalogAlbum>();
        foreach (var album in catalog?.Albums ?? Array.Empty<CatalogAlbum>())
        {
            _byName.TryAdd(PlayHistory.AlbumKey(album.ArtistName, album.Title), album);
        }

        _now = now;
        _trackDays = Math.Max(1, trackDays);
        _percent = listenPercent;
        _message = null;
        _tracks = PlayHistory.Tracks(plays, now.AddDays(-_trackDays));
        _week = PlayHistory.Albums(plays, now.AddDays(-7), listenPercent);
        _month = PlayHistory.Albums(plays, now.AddDays(-30), listenPercent);
        _selected = Math.Clamp(_selected, 0, Math.Max(0, Count - 1));
        SetNeedsDraw();
    }

    /// <summary>Shows another list.</summary>
    public void Show(Section section)
    {
        if (section == _section)
        {
            return;
        }

        _section = section;
        _selected = 0;
        _offset = 0;
        ResetCovers();
        SetNeedsDraw();
    }

    protected override void Paint()
    {
        if (_message is not null)
        {
            Put(2, 1, _message, Theme.Dim);
            return;
        }

        var rowsOnScreen = Math.Max(1, H - HeadRows);
        var total = TotalRows;
        _offset = Math.Clamp(_offset, 0, Math.Max(0, total - rowsOnScreen));

        if (Count == 0)
        {
            Put(2, HeadRows + 1, EmptyText, Theme.Dim);
        }

        var cols = Cols;
        for (var row = _offset / ItemHeight; row * cols < Count; row++)
        {
            var top = HeadRows + row * ItemHeight - _offset;
            if (top >= H)
            {
                break;
            }

            for (var c = 0; c < cols && row * cols + c < Count; c++)
            {
                var i = row * cols + c;
                if (OnTracks)
                {
                    PaintTrack(_tracks[i], i, top);
                }
                else
                {
                    PaintAlbum((_section == Section.Week ? _week : _month)[i], i, top, c);
                }
            }
        }

        // The heading stays where it is: drawn last, over whatever scrolled up under it.
        for (var y = 0; y < HeadRows; y++)
        {
            FillRow(y, 0, W, Theme.Normal);
        }

        PaintHeading();
        PaintScrollbar(total, _offset, rowsOnScreen);
    }

    private string EmptyText => _section switch
    {
        Section.Tracks => $"Nothing played in the last {_trackDays} day(s). Tracks are logged once they have played for half of their length.",
        Section.Week => $"No album listened to ({_percent}% or more) in the last 7 days.",
        _ => $"No album listened to ({_percent}% or more) in the last 30 days.",
    };

    private void PaintHeading()
    {
        _spans.Clear();
        var x = 1;
        foreach (var (section, label) in new[]
        {
            (Section.Tracks, $"Tracks · last {_trackDays} day(s) ({_tracks.Count})"),
            (Section.Week, $"Albums · last week ({_week.Count})"),
            (Section.Month, $"Albums · last month ({_month.Count})"),
        })
        {
            var on = section == _section;
            var end = Put(x, 0, label, on ? new Terminal.Gui.Drawing.Attribute(Theme.Accent, Theme.Bg, Terminal.Gui.Drawing.TextStyle.Underline) : Theme.Dim);
            _spans.Add((x, end, section));
            x = end + 3;
        }

        Put(1, 1, new string('─', Math.Max(0, W - 3)), Theme.Dim);
    }

    private void PaintTrack(PlayRecord play, int index, int y)
    {
        var selected = index == _selected;
        var attr = selected ? (HasFocus ? Theme.Selected : Theme.SelectedDim) : Theme.Normal;
        FillRow(y, 0, W - 1, attr);
        var when = When(play.PlayedAt.ToLocalTime(), _now.ToLocalTime());
        var avail = Math.Max(10, W - 3 - 18);
        var titleW = avail * 40 / 100;
        var artistW = avail * 25 / 100;
        var albumW = avail - titleW - artistW;
        var dim = selected ? attr : new Terminal.Gui.Drawing.Attribute(Theme.FgDim, Theme.Bg);
        Put(1, y, when, dim);
        Put(18, y, Clip(play.Title, titleW - 1), attr);
        Put(18 + titleW, y, Clip(play.Artist, artistW - 1), dim);
        Put(18 + titleW + artistW, y, Clip(play.Album, albumW - 1), dim);
    }

    private void PaintAlbum(AlbumListen listen, int index, int top, int col)
    {
        var album = Resolve(listen.AlbumId, listen.Artist, listen.Album);
        var slotW = SlotW;
        var slotX = col * slotW;
        DrawCover(slotX + (slotW - CoverCols) / 2, top, CoverCols, CoverRows, new CoverKey(album?.ArtworkId, listen.AlbumId), CoverPlace.Library);

        var selected = index == _selected;
        var attr = selected ? (HasFocus ? Theme.Selected : Theme.SelectedDim) : Theme.Normal;
        var room = Math.Max(1, slotW - 2);
        var count = listen.Total > listen.InWindow ? $"{listen.InWindow}× ({listen.Total} in total)" : $"{listen.InWindow}×";
        var lines = new[] { Clip(listen.Album, room), Clip(listen.Artist, room), Clip($"{count} · {Ago(listen.LastListen, _now)}", room) };

        // Every cell of the text rows is written (a cover that was here a moment ago must not show through) and claimed.
        for (var r = 0; r < TextRows; r++)
        {
            FillRow(top + CoverRows + r, slotX, slotX + slotW, Theme.Normal);
        }

        Claim(slotX, top + CoverRows, slotW, TextRows);
        for (var r = 0; r < TextRows; r++)
        {
            var y = top + CoverRows + r;
            if (selected)
            {
                FillRow(y, slotX + 1, slotX + slotW - 1, attr);
            }

            var style = r == 0 ? new Terminal.Gui.Drawing.Attribute(selected ? attr.Foreground : Theme.Fg, selected ? attr.Background : Theme.Bg, Terminal.Gui.Drawing.TextStyle.Bold) : selected ? attr : Theme.Dim;
            Put(slotX + (slotW - lines[r].Length) / 2, y, lines[r], style);
        }
    }
    /// <summary>"Today 14:32", "Yesterday 21:05", else "Mon 10 Nov 18:40" (local times).</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var days = (now.Date - at.Date).Days;
        var time = at.ToString("HH:mm", CultureInfo.InvariantCulture);
        return days switch
        {
            0 => "Today " + time,
            1 => "Yesterday " + time,
            _ => at.ToString("ddd d MMM ", CultureInfo.InvariantCulture) + time,
        };
    }

    /// <summary>A short "2 hours ago" for a moment in the past.</summary>
    public static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var span = now - at;
        if (span < TimeSpan.FromMinutes(1))
        {
            return "moments ago";
        }

        if (span < TimeSpan.FromHours(1))
        {
            return $"{(int)span.TotalMinutes} min ago";
        }

        if (span < TimeSpan.FromDays(1))
        {
            return $"{(int)span.TotalHours} h ago";
        }

        var days = (int)span.TotalDays;
        return days == 1 ? "yesterday" : $"{days} days ago";
    }

    private void Move(int delta)
    {
        if (Count == 0)
        {
            return;
        }

        _selected = Math.Clamp(_selected + delta, 0, Count - 1);
        var rows = Math.Max(1, H - HeadRows);
        var top = _selected / Cols * ItemHeight;
        if (top < _offset)
        {
            _offset = top;
        }
        else if (top + ItemHeight > _offset + rows)
        {
            _offset = top + ItemHeight - rows;
        }

        SetNeedsDraw();
    }

    private void Scroll(int rows)
    {
        _offset = Math.Clamp(_offset + rows, 0, Math.Max(0, TotalRows - Math.Max(1, H - HeadRows)));
        SetNeedsDraw();
    }

    private void Switch(int delta)
    {
        var next = ((int)_section + delta + 3) % 3;
        Show((Section)next);
    }

    /// <summary>
    /// The album in the current index that a logged play belongs to: the one with the logged id when its names still agree, else the one with the same artist and title.
    /// The server hands out new ids after a wipe and rescan, so the log cannot rely on them.
    /// </summary>
    private CatalogAlbum? Resolve(int id, string artist, string title)
    {
        var key = PlayHistory.AlbumKey(artist, title);
        if (_catalog?.Album(id) is { } byId && PlayHistory.AlbumKey(byId.ArtistName, byId.Title) == key)
        {
            return byId;
        }

        return _byName.GetValueOrDefault(key);
    }

    /// <summary>The album of the selected line and its tracks (null when the index does not know it), with the logged track's position for a track line.</summary>
    private (CatalogAlbum Album, IReadOnlyList<CatalogTrack> Tracks, int Index)? Selected()
    {
        if (_catalog is not { } catalog || _selected >= Count)
        {
            return null;
        }

        if (OnTracks)
        {
            var play = _tracks[_selected];
            if (Resolve(play.AlbumId, play.Artist, play.Album) is not { } album)
            {
                return null;
            }

            var tracks = catalog.TracksOf(album.Id);
            var at = tracks.ToList().FindIndex(t => t.Id == play.TrackId && string.Equals(t.Title, play.Title, StringComparison.OrdinalIgnoreCase));
            if (at < 0)
            {
                var title = TextFolding.Fold(play.Title);
                at = tracks.ToList().FindIndex(t => TextFolding.Fold(t.Title) == title);
            }

            return (album, tracks, at);
        }

        var listen = (_section == Section.Week ? _week : _month)[_selected];
        return Resolve(listen.AlbumId, listen.Artist, listen.Album) is { } a ? (a, catalog.TracksOf(a.Id), -1) : null;
    }

    private void Activate()
    {
        if (Selected() is not { } chosen)
        {
            return;
        }

        if (OnTracks)
        {
            if (chosen.Tracks.Count > 0)
            {
                PlayRequested?.Invoke(chosen.Album, chosen.Tracks, Math.Max(0, chosen.Index));
            }
        }
        else
        {
            OpenRequested?.Invoke(chosen.Album);
        }
    }

    private void RaiseContext(System.Drawing.Point screen)
    {
        if (Selected() is { } chosen)
        {
            ContextRequested?.Invoke(chosen.Album, chosen.Tracks, chosen.Index, screen);
        }
    }

    protected override bool OnKeyDown(Key key)
    {
        var cols = Cols;
        var page = Math.Max(1, (H - HeadRows) / ItemHeight) * cols;
        switch (key.KeyCode)
        {
            case KeyCode.CursorDown:
                Move(cols);
                return true;
            case KeyCode.CursorUp:
                Move(-cols);
                return true;
            case KeyCode.CursorRight when !OnTracks:
                Move(1);
                return true;
            case KeyCode.CursorLeft when !OnTracks:
                Move(-1);
                return true;
            case KeyCode.CursorRight:
                Switch(1);
                return true;
            case KeyCode.CursorLeft:
                Switch(-1);
                return true;
            case KeyCode.PageDown:
                Move(page);
                return true;
            case KeyCode.PageUp:
                Move(-page);
                return true;
            case KeyCode.Home:
                Move(-Count);
                return true;
            case KeyCode.End:
                Move(Count);
                return true;
            case KeyCode.Enter:
                Activate();
                return true;
        }

        if (key == Key.Tab)
        {
            Switch(1);
            return true;
        }

        if (key == Key.Tab.WithShift)
        {
            Switch(-1);
            return true;
        }

        return base.OnKeyDown(key);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (StrayClick(mouse))
        {
            return true;
        }

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

        var rows = Math.Max(1, H - HeadRows);
        if (ScrollbarMouse(mouse, TotalRows > rows, y =>
            {
                _offset = OffsetFromScrollbar(y, TotalRows, rows);
                SetNeedsDraw();
            }))
        {
            return true;
        }

        var right = mouse.Flags.HasFlag(MouseFlags.RightButtonClicked);
        var click = mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) || mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked);
        if ((click || right) && mouse.Position is { } p)
        {
            SetFocus();
            if (p.Y == 0)
            {
                foreach (var (x0, x1, section) in _spans)
                {
                    if (click && p.X >= x0 && p.X < x1)
                    {
                        Show(section);
                        break;
                    }
                }

                return true;
            }

            if (p.Y >= HeadRows && p.X < W - 1)
            {
                var index = (p.Y - HeadRows + _offset) / ItemHeight * Cols + Math.Min(Cols - 1, p.X / SlotW);
                if (index < Count)
                {
                    _selected = index;
                    SetNeedsDraw();
                    if (right)
                    {
                        RaiseContext(mouse.ScreenPosition);
                    }
                    else if (mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked))
                    {
                        Activate();
                    }
                }
            }

            return true;
        }

        return base.OnMouseEvent(mouse);
    }
}
