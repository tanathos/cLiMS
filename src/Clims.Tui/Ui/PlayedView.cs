using System.Globalization;
using Clims.Catalog;
using Clims.Tui.Settings;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>
/// The Played tab: what was played lately, from the play log. Three lists, switched with Left / Right or a click on their name:
/// the tracks of the last few days (a setting), and the albums listened to (half of them or more) in the last 7 and the last 30 days.
/// The albums are a grid of covers (as in the Library's grid layout) with title, artist and how often the album was listened to; no tracklists.
/// Enter or a double click opens the album in the Library, a right click offers Play / Enqueue. Tab switches the list too (the arrows move over the covers).
/// The albums can also be shown as a mosaic (see PlayedView.Mosaic.cs).
/// </summary>
public sealed partial class PlayedView : PaintView
{
    private const int HeadRows = 2;

    /// <summary>What a list shows.</summary>
    public enum Section
    {
        Tracks,
        Week,
        Month,
        Genres,
    }

    /// <summary>A line of the genre list: a genre, or one of the genres played along with it.</summary>
    private readonly record struct GenreRow(GenrePlays Item, GenrePlays? Parent);

    private IReadOnlyList<GenrePlays> _genres = Array.Empty<GenrePlays>();
    private readonly HashSet<string> _opened = new(StringComparer.OrdinalIgnoreCase);
    private List<GenreRow> _genreRows = new();
    private List<(PlayRecord Play, IReadOnlyList<string> Genres)> _playGenres = new();
    private List<PlayRecord> _detail = new();
    private string _detailTitle = "";
    private int _detailSelected;
    private int _detailOffset;
    private bool _inDetail;
    private bool _dragDetail;

    private readonly List<(int X0, int X1, Section Section)> _spans = new();
    private MusicCatalog? _catalog;
    private Dictionary<string, CatalogAlbum> _byName = new();
    private IReadOnlyList<PlayRecord> _tracks = Array.Empty<PlayRecord>();
    private IReadOnlyList<AlbumListen> _week = Array.Empty<AlbumListen>();
    private IReadOnlyList<AlbumListen> _month = Array.Empty<AlbumListen>();
    private IReadOnlyList<AlbumListen> _weekByListens = Array.Empty<AlbumListen>();
    private IReadOnlyList<AlbumListen> _monthByListens = Array.Empty<AlbumListen>();
    private PlayedLayout _shownLayout = CoverArt.PlayedLayout;
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

    /// <summary>One row per line, no covers: the tracks and the genres.</summary>
    private bool OnList => _section is Section.Tracks or Section.Genres;

    /// <summary>Rows of text under a cover: the title, the artist and the listens.</summary>
    private const int TextRows = 3;

    /// <summary>Covers across: one list of tracks is a single column.</summary>
    private int Cols => OnList ? 1 : Math.Max(1, (W - 1 - 2) / (CoverCols + 4));

    private int SlotW => OnList ? Math.Max(1, W - 1) : Math.Max(1, (W - 1) / Cols);

    /// <summary>Rows of one line of the list: a track, or a cover with its text and a blank row.</summary>
    private int ItemHeight => OnList ? 1 : CoverRows + TextRows + 1;

    private int TotalRows => OnMosaic ? Mosaic().Height : (Count + Cols - 1) / Cols * ItemHeight;

    /// <summary>The albums of the list on show in the order shown: the latest first in the grid, the most listened first in the mosaic.</summary>
    private IReadOnlyList<AlbumListen> Albums => AlbumsIn(CoverArt.PlayedLayout);

    private IReadOnlyList<AlbumListen> AlbumsIn(PlayedLayout layout) => _section == Section.Week
        ? layout == PlayedLayout.Mosaic ? _weekByListens : _week
        : layout == PlayedLayout.Mosaic ? _monthByListens : _month;

    /// <summary>The list as it was ordered before the layout changed.</summary>
    private IReadOnlyList<AlbumListen> ShownBefore() => AlbumsIn(_shownLayout);

    private int Count => _section switch
    {
        Section.Tracks => _tracks.Count,
        Section.Week => _week.Count,
        Section.Genres => _genreRows.Count,
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
        _weekByListens = ByListens(_week);
        _monthByListens = ByListens(_month);
        _shownLayout = CoverArt.PlayedLayout;
        _playGenres = catalog is null ? new() : plays.Select(p => (p, GenresOfPlay(catalog, p))).ToList();
        var lookup = _playGenres.ToDictionary(x => x.Play, x => x.Genres, ReferenceEqualityComparer.Instance as IEqualityComparer<PlayRecord>);
        _genres = catalog is null ? Array.Empty<GenrePlays>() : PlayHistory.Genres(plays, DateTimeOffset.MinValue, play => lookup[play]);
        _detail = new List<PlayRecord>();
        _inDetail = false;
        RebuildGenreRows();
        _selected = Math.Clamp(_selected, 0, Math.Max(0, Count - 1));
        SetNeedsDraw();
    }

    /// <summary>The most listened first; among equals the order given (the latest first) is kept.</summary>
    private static IReadOnlyList<AlbumListen> ByListens(IReadOnlyList<AlbumListen> albums) => albums.OrderByDescending(a => a.InWindow).ToList();

    private IReadOnlyList<string> GenresOfPlay(MusicCatalog catalog, PlayRecord play)
    {
        if (Resolve(play.AlbumId, play.Artist, play.Album) is not { } album)
        {
            return Array.Empty<string>();
        }

        var tracks = catalog.TracksOf(album.Id);
        var title = TextFolding.Fold(play.Title);
        var track = tracks.FirstOrDefault(t => t.Id == play.TrackId && TextFolding.Fold(t.Title) == title)
            ?? tracks.FirstOrDefault(t => TextFolding.Fold(t.Title) == title);
        return track is null ? Array.Empty<string>() : catalog.GenresOfTrack(track);
    }

    private void RebuildGenreRows()
    {
        _genreRows = new List<GenreRow>();
        foreach (var genre in _genres)
        {
            _genreRows.Add(new GenreRow(genre, null));
            if (_opened.Contains(genre.Genre))
            {
                _genreRows.AddRange(genre.Together.Select(c => new GenreRow(c, genre)));
            }
        }
    }

    /// <summary>Opens or closes the co-genres under the selected genre (a co-genre line closes its genre).</summary>
    private void ToggleGenre()
    {
        if (_selected >= _genreRows.Count)
        {
            return;
        }

        var row = _genreRows[_selected];
        var genre = row.Parent ?? row.Item;
        if (genre.Together.Count == 0 || !_opened.Add(genre.Genre))
        {
            _opened.Remove(genre.Genre);
        }

        RebuildGenreRows();
        _selected = Math.Max(0, _genreRows.FindIndex(r => r.Parent is null && r.Item.Genre == genre.Genre));
        Move(0);
    }

    /// <summary>Shows another list.</summary>
    public void Show(Section section)
    {
        if (section == _section)
        {
            return;
        }

        _section = section;
        CloseDetail();
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

        var rowsOnScreen = Math.Max(1, ListRows);
        var total = TotalRows;
        _offset = Math.Clamp(_offset, 0, Math.Max(0, total - rowsOnScreen));

        if (Count == 0)
        {
            Put(2, HeadRows + 1, EmptyText, Theme.Dim);
        }

        var cols = Cols;
        for (var row = _offset / ItemHeight; !OnMosaic && row * cols < Count; row++)
        {
            var top = HeadRows + row * ItemHeight - _offset;
            if (top >= HeadRows + ListRows)
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
                else if (_section == Section.Genres)
                {
                    PaintGenre(_genreRows[i], i, top);
                }
                else
                {
                    PaintAlbum(Albums[i], i, top, c);
                }
            }
        }

        if (OnMosaic)
        {
            PaintMosaic();
        }

        // The heading stays where it is: drawn last, over whatever scrolled up under it.
        for (var y = 0; y < HeadRows; y++)
        {
            FillRow(y, 0, W, Theme.Normal);
        }

        PaintHeading();
        PaintScrollbar(total, _offset, rowsOnScreen, HeadRows, rowsOnScreen);
        PaintDetail();
    }

    /// <summary>The bottom half under the genre list: the plays of the genre that was double clicked, newest first.</summary>
    private void PaintDetail()
    {
        if (!DetailOpen)
        {
            return;
        }

        var top = DetailTop;
        FillRow(top, 0, W, Theme.Normal);
        Put(1, top, Clip($"─ Plays of {_detailTitle} ({_detail.Count}) · Esc close · Tab switch · Enter play · t opens ", Math.Max(1, W - 3)).PadRight(Math.Max(0, W - 3), '─'), Theme.Dim);
        var rows = DetailRows;
        _detailSelected = Math.Clamp(_detailSelected, 0, Math.Max(0, _detail.Count - 1));
        _detailOffset = Math.Clamp(_detailOffset, 0, Math.Max(0, _detail.Count - rows));
        if (_detailSelected < _detailOffset)
        {
            _detailOffset = _detailSelected;
        }
        else if (_detailSelected >= _detailOffset + rows)
        {
            _detailOffset = _detailSelected - rows + 1;
        }

        for (var r = 0; r < rows; r++)
        {
            var i = _detailOffset + r;
            if (i < _detail.Count)
            {
                PaintTrack(_detail[i], i, top + 1 + r, _inDetail && i == _detailSelected);
            }
            else
            {
                FillRow(top + 1 + r, 0, W - 1, Theme.Normal);
            }
        }

        PaintScrollbar(_detail.Count, _detailOffset, rows, top + 1, rows);
    }

    private bool DetailOpen => _section == Section.Genres && _detail.Count > 0 && H > HeadRows + 8;

    private int DetailTop => HeadRows + ListRows;

    private int DetailRows => Math.Max(1, H - DetailTop - 1);

    /// <summary>Rows of the main list: all of them, or the top half while the plays of a genre are shown.</summary>
    private int ListRows => _section == Section.Genres && _detail.Count > 0 && H > HeadRows + 8 ? Math.Max(3, (H - HeadRows) / 2)
        : OnMosaic ? Math.Max(1, H - HeadRows - 1) : H - HeadRows;

    private void OpenDetail()
    {
        if (_selected >= _genreRows.Count)
        {
            return;
        }

        var row = _genreRows[_selected];
        var wanted = row.Parent is { } parent ? new[] { parent.Genre, row.Item.Genre } : new[] { row.Item.Genre };
        _detailTitle = string.Join(" + ", wanted);
        _detail = _playGenres
            .Where(p => wanted.All(w => p.Genres.Contains(w, StringComparer.OrdinalIgnoreCase)))
            .Select(p => p.Play).OrderByDescending(p => p.PlayedAt).ToList();
        _detailSelected = 0;
        _detailOffset = 0;
        _inDetail = false;
        SetNeedsDraw();
    }

    private void CloseDetail()
    {
        _detail = new List<PlayRecord>();
        _inDetail = false;
        SetNeedsDraw();
    }

    private void PlayDetail()
    {
        if (_catalog is not { } catalog || _detailSelected >= _detail.Count)
        {
            return;
        }

        var play = _detail[_detailSelected];
        if (Resolve(play.AlbumId, play.Artist, play.Album) is not { } album)
        {
            return;
        }

        var tracks = catalog.TracksOf(album.Id);
        var title = TextFolding.Fold(play.Title);
        var at = tracks.ToList().FindIndex(t => TextFolding.Fold(t.Title) == title);
        if (tracks.Count > 0)
        {
            PlayRequested?.Invoke(album, tracks, Math.Max(0, at));
        }
    }

    private string EmptyText => _section switch
    {
        Section.Tracks => $"Nothing played in the last {_trackDays} day(s). Tracks are logged once they have played for half of their length.",
        Section.Week => $"No album listened to ({_percent}% or more) in the last 7 days.",
        Section.Genres => "No genre to show: tracks are logged once played, and genres come from the library index.",
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
            (Section.Genres, $"Genres · most played ({_genres.Count})"),
        })
        {
            var on = section == _section;
            var end = Put(x, 0, label, on ? new Terminal.Gui.Drawing.Attribute(Theme.Accent, Theme.Bg, Terminal.Gui.Drawing.TextStyle.Underline) : Theme.Dim);
            _spans.Add((x, end, section));
            x = end + 3;
        }

        Put(1, 1, new string('─', Math.Max(0, W - 3)), Theme.Dim);
    }

    private void PaintTrack(PlayRecord play, int index, int y, bool? selectedRow = null)
    {
        var selected = selectedRow ?? index == _selected;
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

    private void PaintGenre(GenreRow row, int index, int y)
    {
        var selected = index == _selected;
        var attr = selected ? (HasFocus ? Theme.Selected : Theme.SelectedDim) : Theme.Normal;
        var dim = selected ? attr : new Terminal.Gui.Drawing.Attribute(Theme.FgDim, Theme.Bg);
        FillRow(y, 0, W - 1, attr);

        var item = row.Item;
        string left;
        string right;
        if (row.Parent is { } parent)
        {
            left = "      + " + item.Genre;
            right = $"{item.Plays}  ({item.Plays * 100 / Math.Max(1, parent.Plays)}% of {parent.Genre})";
        }
        else
        {
            var total = Math.Max(1, _genres.Max(g => g.Plays));
            var mark = item.Together.Count == 0 ? " " : _opened.Contains(item.Genre) ? "▾" : "▸";
            left = $" {mark} {item.Genre}";
            right = $"{item.Plays} play(s)  {new string('█', Math.Max(1, item.Plays * 20 / total))}";
        }

        var room = Math.Max(10, W - 3);
        Put(1, y, Clip(left, room - right.Length - 1), row.Parent is null ? attr : dim);
        Put(Math.Max(1, room - right.Length), y, right, dim);
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
        var lines = new[] { Clip(listen.Album, room), Clip(listen.Artist, room), Clip($"{Listens(listen)} · {Ago(listen.LastListen, _now)}", room) };

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

    private static string Listens(AlbumListen listen) =>
        listen.Total > listen.InWindow ? $"{listen.InWindow}× ({listen.Total} in total)" : $"{listen.InWindow}×";

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

        if (OnMosaic)
        {
            SelectTile(_selected + delta);
            return;
        }

        _selected = Math.Clamp(_selected + delta, 0, Count - 1);
        var rows = Math.Max(1, ListRows);
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
        _offset = Math.Clamp(_offset + rows, 0, Math.Max(0, TotalRows - Math.Max(1, ListRows)));
        SetNeedsDraw();
    }

    private void Switch(int delta)
    {
        var next = ((int)_section + delta + 4) % 4;
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
        if (_catalog is not { } catalog || _selected >= Count || _section == Section.Genres)
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

        var listen = Albums[_selected];
        return Resolve(listen.AlbumId, listen.Artist, listen.Album) is { } a ? (a, catalog.TracksOf(a.Id), -1) : null;
    }

    private void Activate()
    {
        if (_section == Section.Genres)
        {
            ToggleGenre();
            return;
        }

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
        if (DetailOpen)
        {
            if (key == Key.Esc)
            {
                CloseDetail();
                return true;
            }

            if (key == Key.Tab || key == Key.Tab.WithShift)
            {
                _inDetail = !_inDetail;
                SetNeedsDraw();
                return true;
            }

            if (_inDetail)
            {
                var step = key.KeyCode switch
                {
                    KeyCode.CursorDown => 1,
                    KeyCode.CursorUp => -1,
                    KeyCode.PageDown => Math.Max(1, H - DetailTop - 2),
                    KeyCode.PageUp => -Math.Max(1, H - DetailTop - 2),
                    KeyCode.Home => -_detail.Count,
                    KeyCode.End => _detail.Count,
                    _ => 0,
                };
                if (step != 0)
                {
                    _detailSelected = Math.Clamp(_detailSelected + step, 0, _detail.Count - 1);
                    SetNeedsDraw();
                    return true;
                }

                if (key.KeyCode == KeyCode.Enter)
                {
                    PlayDetail();
                    return true;
                }
            }
        }

        if (_section == Section.Genres && key == Key.T && !_inDetail && _selected < _genreRows.Count)
        {
            OpenDetail();
            return true;
        }

        if (OnMosaic && MosaicKey(key))
        {
            return true;
        }

        var cols = Cols;
        var page =Math.Max(1, ListRows / ItemHeight) * cols;
        switch (key.KeyCode)
        {
            case KeyCode.CursorDown:
                Move(cols);
                return true;
            case KeyCode.CursorUp:
                Move(-cols);
                return true;
            case KeyCode.Space when _section == Section.Genres:
                ToggleGenre();
                return true;
            case KeyCode.CursorRight when !OnList:
                Move(1);
                return true;
            case KeyCode.CursorLeft when !OnList:
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

        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown) || mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            var step = (mouse.Flags.HasFlag(MouseFlags.WheeledDown) ? 1 : -1) * (OnMosaic ? WheelRows : 3);
            if (DetailOpen && mouse.Position is { } wheel && wheel.Y > DetailTop)
            {
                _detailOffset = Math.Clamp(_detailOffset + step, 0, Math.Max(0, _detail.Count - DetailRows));
                SetNeedsDraw();
            }
            else
            {
                Scroll(step);
            }

            return true;
        }

        var rows = Math.Max(1, ListRows);
        var pressed = mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) && !mouse.Flags.HasFlag(MouseFlags.PositionReport);
        if (pressed && mouse.Position is { } down)
        {
            _dragDetail = DetailOpen && down.Y > DetailTop;
        }

        if (DetailOpen && _dragDetail)
        {
            if (ScrollbarMouse(mouse, _detail.Count > DetailRows, y =>
                {
                    _detailOffset = OffsetFromScrollbar(y, _detail.Count, DetailRows, DetailRows);
                    SetNeedsDraw();
                }, DetailTop + 1, DetailRows))
            {
                return true;
            }
        }
        else if (ScrollbarMouse(mouse, TotalRows > rows, y =>
            {
                _offset = OffsetFromScrollbar(y, TotalRows, rows, rows);
                SetNeedsDraw();
            }, HeadRows, rows))
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

            if (_section == Section.Genres && DetailOpen && p.Y > DetailTop)
            {
                _inDetail = true;
                var at = p.Y - DetailTop - 1 + _detailOffset;
                if (at >= 0 && at < _detail.Count)
                {
                    _detailSelected = at;
                    if (mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked))
                    {
                        PlayDetail();
                    }
                }

                SetNeedsDraw();
                return true;
            }

            if (_section == Section.Genres && p.Y >= HeadRows && p.Y < HeadRows + ListRows && p.X < W - 1)
            {
                _inDetail = false;
                var at = p.Y - HeadRows + _offset;
                if (at < _genreRows.Count)
                {
                    _selected = at;
                    SetNeedsDraw();
                    if (mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked))
                    {
                        OpenDetail();
                    }
                    else if (click && _genreRows[at].Parent is null && p.X <= 4)
                    {
                        ToggleGenre(); // a single click on the arrow
                    }
                }

                return true;
            }

            if (p.Y >= HeadRows && p.X < W - 1)
            {
                var index = OnMosaic ? TileAt(p.X, p.Y) : (p.Y - HeadRows + _offset) / ItemHeight * Cols + Math.Min(Cols - 1, p.X / SlotW);
                if (index >= 0 && index < Count)
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
