using System.Globalization;
using Clims.Catalog;
using Clims.Lyrics;
using Clims.Tui.Data;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>
/// Full-screen view of the playing album, opened by a click on the player's cover: the cover on the left, and on the
/// right the artist, album title, genres, runtime and file format, the lyrics, the progress bars and the playback controls. Esc closes it.
/// </summary>
public sealed class AlbumInfoOverlay : PaintView
{
    private static readonly HashSet<string> Lossless = new(StringComparer.OrdinalIgnoreCase) { "flac", "wav", "aiff", "aif", "ape", "wv", "alac", "dsf", "dff" };

    private NowPlaying? _now;
    private MusicCatalog? _catalog;
    private LyricsState _lyricsState = LyricsState.Idle;
    private LyricsResult? _lyrics;
    private bool _canEnableOnline;
    private double _offset;
    private TimeSpan _elapsed;
    private int _manual;
    private int _lastLine = -2;
    private int _bodyHeight = 1;
    private DateTime _scrolledAt = DateTime.MinValue;
    private IReadOnlyList<TimeSpan> _queue = [];
    private int _queueIndex;
    private int _shownSecond = -1;
    private bool _countdown;
    private (int X0, int X1, int Y) _trackBar;
    private (bool? Playing, bool? Shuffle, bool Live) _controlState;
    private readonly List<(int X0, int X1, int Y, Action Run)> _buttons = [];
    private readonly List<(int X0, int X1, int Y)> _totals = [];

    /// <summary>The player the controls under the progress bar drive; without one they are not shown.</summary>
    public Data.IPlayerController? Player { get; set; }

    protected override bool TracksHover => true;

    /// <summary>The name to show for where a streamed track comes from; null: no tag.</summary>
    public Func<NowPlaying, string?>? SourceName { get; set; }

    public AlbumInfoOverlay()
    {
        CanFocus = true;
    }

    /// <summary>The user clicked, or pressed a key that closes the view.</summary>
    public event Action? CloseRequested;

    protected override Attribute Base => new(Theme.Fg, Theme.Bg);

    protected override bool Dimmable => false;

    // The footer under this view redraws ten times a second and drags this view along: the cover must not be sent again each time.
    protected override bool StableSixels => true;

    public void Show(NowPlaying? now, MusicCatalog? catalog)
    {
        // The player reports a change every second (the elapsed time). Painting again over a still cover would erase its
        // image (any text written over a sixel does), so only a different track or album repaints.
        if (_now is { } old && now is { } next && old.Album.Id == next.Album.Id && old.Track.Id == next.Track.Id && ReferenceEquals(_catalog, catalog))
        {
            return;
        }

        var newAlbum = _now?.Album.Id != now?.Album.Id;
        // A new track starts its lyrics from the top, with the timing the file came with.
        (_offset, _manual, _lastLine) = (0, 0, -2);
        _now = now;
        _catalog = catalog;
        if (newAlbum)
        {
            ResetCovers();
        }

        SetNeedsDraw();
    }

    /// <summary>The distinct file formats of the album (from the file addresses the index knows), upper case, and whether all are lossless.</summary>
    public static (IReadOnlyList<string> Formats, bool AllLossless) FormatsOf(IEnumerable<string?> urls)
    {
        var formats = new List<string>();
        foreach (var url in urls)
        {
            var ext = ExtensionOf(url);
            if (ext.Length > 0 && !formats.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                formats.Add(ext);
            }
        }

        return (formats.Select(f => f.ToUpperInvariant()).ToList(), formats.Count > 0 && formats.All(Lossless.Contains));
    }

    private static string ExtensionOf(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return "";
        }

        var path = url.Split('?', '#')[0];
        var dot = path.LastIndexOf('.');
        var slash = path.LastIndexOfAny(['/', '\\']);
        return dot > slash && dot < path.Length - 1 && path.Length - dot <= 6 ? path[(dot + 1)..] : "";
    }

    protected override void Paint()
    {
        if (_now is not { } now)
        {
            return;
        }

        var album = now.Album;
        var frame = new Attribute(Theme.Border, Theme.Bg);
        var dim = Theme.Dim;
        // As tall as the panel; the width follows from the shape of a cell, so the cover stays square.
        var cell = SixelCovers.CellPixels;
        var rows = Math.Max(2, H - 2);
        var cols = Math.Max(2, rows * cell.Height / Math.Max(1, cell.Width));
        var maxCols = Math.Max(8, W * 55 / 100);
        if (cols > maxCols)
        {
            cols = maxCols;
            rows = Math.Max(4, cols * cell.Width / Math.Max(1, cell.Height));
        }

        // One column of margin on the left, one row above and below.
        const int left = 1;
        var top = 1 + (Math.Max(2, H - 2) - rows) / 2;
        var big = new CoverKey(album.ArtworkId, album.Id, Big: true);
        var small = new CoverKey(album.ArtworkId, album.Id);
        // The large picture takes a moment to arrive: show the list-size one meanwhile.
        WantCover(big);
        DrawCover(left, top, cols, rows, CoverArt.Source.TryGet(big) is null ? small : big, CoverPlace.FullScreen);

        var x = left + cols + 5;
        var width = W - x - 3;
        var hint = "Esc close";
        if (_lyrics is { Instrumental: false })
        {
            hint += "  ·  ↑↓ scroll" + (_lyrics.IsSynced ? "  ·  [ ] timing" : "") + "  ·  R search again";
        }

        Put(x, H - 2, Clip(hint, W - x), dim); // one empty line below it, so it does not touch the border
        if (width < 8)
        {
            return;
        }

        for (var y = top; y < top + rows; y++)
        {
            Put(x - 3, y, "│", frame);
        }

        var summary = _catalog?.Summary(album.Id);
        var genres = summary is { Genres.Count: > 0 } ? string.Join("; ", summary.Genres) : (now.Track.Genre ?? "");
        var runtime = now.Track.Duration > TimeSpan.Zero ? FormatTime(now.Track.Duration) : "unknown";
        var urls = (_catalog?.TracksOf(album.Id).Select(t => t.Url) ?? []).Append(now.Track.Url);
        var (formats, lossless) = FormatsOf(urls);

        var artist = album.ArtistName.Length > 0 ? album.ArtistName : now.Track.Artist ?? "";
        // Anchored to the top of the cover: the lyrics take the rest of the column below.
        var y0 = top;
        var from = SourceName?.Invoke(now) is { Length: > 0 } source ? $"[From {source}] " : "";
        var artistX = Put(x, y0, Clip(from, width), dim);
        Put(artistX, y0, Clip(artist, Math.Max(0, width - from.Length)), new Attribute(Theme.Accent, Theme.Bg));
        var yearText = album.Year > 0 ? "  " + album.Year.ToString(CultureInfo.InvariantCulture) : "";
        var titleEnd = Put(x, y0 + 1, Clip(album.Title, width - yearText.Length), new Attribute(Theme.Fg, Theme.Bg, TextStyle.Bold));
        Put(titleEnd, y0 + 1, yearText, dim);
        Put(x, y0 + 2, new string('─', Math.Min(width, 40)), frame);

        const int labelW = 10;
        var label = new Attribute(Theme.FgDim, Theme.Bg);
        var value = new Attribute(Theme.Fg, Theme.Bg);
        var line = y0 + 4;
        Put(x, line, "Track", label);
        var trackName = now.Track.Number > 0 ? $"{now.Track.Number}. {now.Track.Title}" : now.Track.Title;
        Put(x + labelW, line, Clip(trackName, width - labelW), new Attribute(Theme.Fg, Theme.Bg, TextStyle.Bold));
        line += 1;
        Put(x, line, "Genre", label);
        Put(x + labelW, line, Clip(genres.Length > 0 ? genres : "unknown", width - labelW), new Attribute(Theme.Fg, Theme.Bg, TextStyle.Italic));
        line += 1;
        Put(x, line, "Runtime", label);
        Put(x + labelW, line, runtime, value);
        line += 1;
        Put(x, line, "Format", label);
        if (formats.Count == 0)
        {
            Put(x + labelW, line, "unknown", value);
        }
        else
        {
            var cx = Put(x + labelW, line, lossless ? "◆ " : "◇ ", new Attribute(lossless ? Theme.Accent : Theme.FgDim, Theme.Bg));
            cx = Put(cx, line, string.Join(" / ", formats), new Attribute(Theme.Fg, Theme.Bg, TextStyle.Bold));
            Put(cx, line, lossless ? "  lossless" : "  lossy", dim);
        }

        // From the bottom: the hint, an empty row, the playback controls, then the progress bars (the playlist's, when there is one, above the track's).
        _buttons.Clear();
        _totals.Clear();
        var multi = _queue.Count > 1;
        var barsTop = H - (multi ? 6 : 5);
        if (multi)
        {
            PaintQueueBar(x, barsTop, width, now.Track.Duration);
        }

        PaintTrackBar(x, barsTop + (multi ? 1 : 0), width, now.Track.Duration);
        PaintControls(x, barsTop + (multi ? 2 : 1), width);
        PaintLyrics(x, y0 + 9, y0 + 10, barsTop - 2, width);
    }

    /// <summary>Widths of the fields left (played time) and right (total or remaining time) of the bars, the same for both bars so they line up.</summary>
    private (int Left, int Right) Fields(TimeSpan trackLength)
    {
        var longest = FormatTime(trackLength).Length;
        if (_queue.Count > 1)
        {
            longest = Math.Max(longest, FormatTime(TimeSpan.FromTicks(_queue.Sum(d => d.Ticks))).Length);
        }

        return (longest + 1, longest + 2);   // room for a space, and on the right for the "-" of the countdown
    }

    /// <summary>The time at the right end of a bar: the total, or with the countdown on the minus time that is left. Right-aligned; a click on it switches.</summary>
    private void PaintRightTime(int x, int y, int width, TimeSpan total, TimeSpan done)
    {
        var text = _countdown ? "-" + FormatTime(total - done > TimeSpan.Zero ? total - done : TimeSpan.Zero) : FormatTime(total);
        var start = x + width - text.Length;
        Put(start, y, text, Over(start, start + text.Length, y) ? Theme.Hover : new Attribute(Theme.FgDim, Theme.Bg));
        _totals.Add((start, start + text.Length, y));
    }

    private void PaintControls(int x, int y, int width)
    {
        if (Player is not { } player)
        {
            return;
        }

        var live = player.Current?.Station is null;   // a radio stream cannot skip or seek
        var items = new (string Label, Action Run, bool Enabled, bool Active)[]
        {
            ("|◀", player.Previous, live, false),
            ("◀◀", () => player.Seek(TimeSpan.FromSeconds(-10)), live, false),
            (player.IsPlaying ? "❚❚" : "▶", player.TogglePlay, true, false),
            ("▶▶", () => player.Seek(TimeSpan.FromSeconds(10)), live, false),
            ("▶|", player.Next, live, false),
            ("⇄", player.ToggleShuffle, true, player.Shuffle),
        };
        var total = items.Sum(i => i.Label.Length + 4) + 2 * (items.Length - 1);
        var cx = x + Math.Max(0, (width - total) / 2);
        foreach (var (label, run, enabled, active) in items)
        {
            var text = $"[ {label} ]";
            if (!enabled)
            {
                Put(cx, y, text, new Attribute(Theme.Faint, Theme.BgAlt));
            }
            else
            {
                Put(cx, y, text, Over(cx, cx + text.Length, y) ? Theme.Hover : new Attribute(active ? Theme.Accent : Theme.Fg, Theme.BgAlt));
                _buttons.Add((cx, cx + text.Length, y, run));
            }

            cx += text.Length + 2;
        }
    }

    private void PaintTrackBar(int x, int y, int width, TimeSpan length)
    {
        var (leftW, rightW) = Fields(length);
        var barW = width - leftW - rightW;
        if (barW < 4)
        {
            return;
        }

        var shown = length > TimeSpan.Zero && _elapsed > length ? length : _elapsed;
        var sections = Sections([length], barW);
        var filled = sections.Count > 0 && length > TimeSpan.Zero ? sections[0].End * shown.TotalSeconds / length.TotalSeconds : 0;
        Put(x, y, FormatTime(shown).PadRight(leftW), new Attribute(Theme.Fg, Theme.Bg));
        DrawBar(x + leftW, y, barW, filled, sections);

        // Pointing past the playhead: the stretch a click would skip is drawn in a faint accent, as in the footer.
        var barX = x + leftW;
        var hover = -1;
        for (var c = (int)Math.Ceiling(filled / 2); c < barW; c++)
        {
            if (Over(barX + c, barX + c + 1, y))
            {
                hover = c;
            }
        }

        if (hover >= 0)
        {
            var from = (int)Math.Ceiling(filled / 2);
            Put(barX + from, y, new string('━', hover - from + 1), new Attribute(Theme.Mix(Theme.Accent, Theme.Bg, 0.5), Theme.Bg));
        }

        _trackBar = length > TimeSpan.Zero ? (barX, barX + barW, y) : (0, 0, 0);
        PaintRightTime(x, y, width, length, shown);
    }

    private void PaintQueueBar(int x, int y, int width, TimeSpan trackLength)
    {
        var (leftW, rightW) = Fields(trackLength);
        var barW = width - leftW - rightW;
        var total = TimeSpan.FromTicks(_queue.Sum(d => d.Ticks));
        if (barW < 4 || total <= TimeSpan.Zero)
        {
            return;
        }

        var before = TimeSpan.FromTicks(_queue.Take(Math.Max(0, _queueIndex)).Sum(d => d.Ticks));
        var done = before + _elapsed;
        var sections = Sections(_queue, barW);
        // The playhead is inside the playing track's section; the gaps between tracks are skipped over.
        double filled = 0;
        if (_queueIndex >= 0 && _queueIndex < sections.Count)
        {
            var (start, end) = sections[_queueIndex];
            var length = _queue[_queueIndex].TotalSeconds;
            filled = start + (length > 0 ? Math.Min(1.0, _elapsed.TotalSeconds / length) : 0) * (end - start);
        }

        Put(x, y, FormatTime(done).PadRight(leftW), new Attribute(Theme.Fg, Theme.Bg));
        DrawBar(x + leftW, y, barW, filled, sections);
        PaintRightTime(x, y, width, total, done);
    }

    /// <summary>Draws the sections as heavy lines with blank gaps between them; <paramref name="filled"/> is the playhead in half cells.</summary>
    private void DrawBar(int x, int y, int cells, double filled, IReadOnlyList<(int Start, int End)> sections)
    {
        var on = new Attribute(Theme.Accent, Theme.Bg);
        var off = new Attribute(Theme.Faint, Theme.Bg);
        int At(int unit) => sections.ToList().FindIndex(s => unit >= s.Start && unit < s.End);
        // No room for gaps: neighbouring tracks are told apart by alternating shades instead.
        var touching = sections.Count > 1 && sections[1].Start == sections[0].End;
        // Four shades, in an order where neighbours always differ a lot.
        double[] steps = [1.0, 0.6, 0.82, 0.45];
        Color Shade(int section, bool played) => played
            ? Theme.Mix(Theme.Bg, Theme.Accent, steps[section % 4])
            : Theme.Mix(Theme.Bg, Theme.Marker, steps[section % 4]);
        for (var c = 0; c < cells; c++)
        {
            int l = At(2 * c), r = At(2 * c + 1);
            if (l < 0 && r < 0)
            {
                continue;
            }

            // Two tracks meeting in one cell: the one that starts there wins (a line has one colour per cell).
            var glyph = l >= 0 && r >= 0 ? "━" : l >= 0 ? "╸" : "╺";
            var played = filled > (l >= 0 ? 2 * c : 2 * c + 1);
            Put(x + c, y, glyph, touching ? new Attribute(Shade(r >= 0 ? r : l, played), Theme.Bg) : played ? on : off);
        }
    }

    /// <summary>
    /// The tracks as stretches of a bar <paramref name="cells"/> wide, in half cells (start inclusive, end exclusive), each as
    /// long as the track and separated by a constant gap of one cell (half a cell, or none, when there are too many tracks to fit).
    /// </summary>
    public static IReadOnlyList<(int Start, int End)> Sections(IReadOnlyList<TimeSpan> durations, int cells)
    {
        var result = new List<(int, int)>();
        var total = durations.Sum(d => d.TotalSeconds);
        var units = cells * 2;
        if (total <= 0 || units < 4)
        {
            return result;
        }

        var gaps = durations.Count - 1;
        var gap = 2;
        while (gap > 0 && gap * gaps > units / 2)
        {
            gap--;
        }

        var room = units - gap * gaps;
        double cumulative = 0;
        var start = 0;
        for (var i = 0; i < durations.Count; i++)
        {
            cumulative += durations[i].TotalSeconds;
            var end = i == durations.Count - 1 ? units : (int)Math.Round(room * cumulative / total) + gap * i;
            end = Math.Min(units, Math.Max(end, start + 1));
            result.Add((start, end));
            start = end + gap;
        }

        return result;
    }

    /// <summary>m:ss, or h:mm:ss from one hour on.</summary>
    public static string FormatTime(TimeSpan time)
    {
        var seconds = (long)Math.Max(0, Math.Floor(time.TotalSeconds));
        return seconds >= 3600
            ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
            : $"{seconds / 60}:{seconds % 60:00}";
    }

    /// <summary>The lengths of the queued tracks in play order, and which one is playing; drives the playlist bar.</summary>
    public void SetQueue(IReadOnlyList<TimeSpan> durations, int index)
    {
        if (index == _queueIndex && durations.SequenceEqual(_queue))
        {
            return;
        }

        (_queue, _queueIndex) = (durations.ToList(), index);
        SetNeedsDraw();
    }

    private void PaintLyrics(int x, int headerY, int bodyTop, int bodyBottom, int width)
    {
        _bodyHeight = Math.Max(1, bodyBottom - bodyTop + 1);
        if (_lyricsState == LyricsState.Idle || bodyBottom - bodyTop + 1 < 3)
        {
            return;
        }

        var dim = new Attribute(Theme.FgDim, Theme.Bg);
        var normal = new Attribute(Theme.Fg, Theme.Bg);
        var head = "Lyrics";
        if (_lyrics is { Instrumental: false } found)
        {
            head += $"  ·  {found.Source}  ·  {(found.IsSynced ? "synced" : "plain")}";
            if (found.IsSynced && _offset != 0)
            {
                head += "  ·  offset " + _offset.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + " s";
            }
        }

        Put(x, headerY, Clip(head, width), dim);
        var height = bodyBottom - bodyTop + 1;
        if (_lyrics is null)
        {
            var message = _lyricsState switch
            {
                LyricsState.Loading => "Looking for lyrics…",
                LyricsState.NotFound => "No lyrics found. Press R to search again.",
                LyricsState.Off when _canEnableOnline => "Nothing stored for this track. Press L to look it up online (the artist, album, title and length are sent to LRCLIB).",
                _ => "Nothing stored for this track.",
            };
            var wrapped = LyricsLayout.Rows([message], width);
            for (var i = 0; i < Math.Min(wrapped.Count, height); i++)
            {
                Put(x, bodyTop + i, wrapped[i].Text, dim);
            }

            return;
        }

        if (_lyrics.Instrumental)
        {
            Put(x, bodyTop, "Instrumental", dim);
            return;
        }

        var synced = _lyrics.IsSynced;
        var texts = synced ? _lyrics.Lines.Select(l => l.Text).ToList() : _lyrics.Plain.Split('\n').ToList();
        var rows = LyricsLayout.Rows(texts, width);
        var current = synced ? LrcParser.IndexAt(_lyrics.Lines, _elapsed + TimeSpan.FromSeconds(_offset)) : -1;
        var focus = current < 0 ? -1 : LyricsLayout.FirstRowOf(rows, current);
        var follow = LyricsLayout.Top(rows.Count, height, focus, 0);
        var first = LyricsLayout.Top(rows.Count, height, focus, _manual);
        _manual = first - follow;
        for (var i = 0; i < height && first + i < rows.Count; i++)
        {
            var row = rows[first + i];
            // Lines already sung fade out the further back they are: 75 %, 50 %, then 25 % of the text colour.
            var attr = !synced ? normal
                : row.Line == current ? new Attribute(Theme.Accent, Theme.Bg, TextStyle.Bold)
                : row.Line < current ? new Attribute(Theme.Mix(Theme.Bg, Theme.Fg, Math.Max(0.25, 1 - 0.25 * (current - row.Line))), Theme.Bg)
                : normal;
            Put(x, bodyTop + i, row.Text, attr);
        }

        if (first > 0)
        {
            Put(x + width + 1, bodyTop, "↑", dim);
        }

        if (first + height < rows.Count)
        {
            Put(x + width + 1, bodyBottom, "↓", dim);
        }
    }

    /// <summary>What the album view shows about the lyrics: where the search stands, the lyrics, and whether online lookup can still be offered.</summary>
    public void SetLyrics(LyricsState state, LyricsResult? lyrics, bool canEnableOnline)
    {
        if (state == _lyricsState && ReferenceEquals(lyrics, _lyrics) && canEnableOnline == _canEnableOnline)
        {
            return;
        }

        if (!ReferenceEquals(lyrics, _lyrics))
        {
            _manual = 0;
            _lastLine = -2;
        }

        (_lyricsState, _lyrics, _canEnableOnline) = (state, lyrics, canEnableOnline);
        SetNeedsDraw();
    }

    /// <summary>
    /// Follows playback: the synced line being sung is highlighted and kept in the middle. A scroll by hand holds for a few
    /// seconds, then the view follows the music again.
    /// </summary>
    public void Tick(TimeSpan elapsed, DateTime now)
    {
        _elapsed = elapsed;
        var second = (int)elapsed.TotalSeconds;
        var controls = (Player?.IsPlaying, Player?.Shuffle, Player?.Current?.Station is null);
        if (second != _shownSecond || controls != _controlState)
        {
            _shownSecond = second;
            _controlState = controls;
            SetNeedsDraw(); // the progress bars and the controls
        }

        if (_lyrics is not { IsSynced: true } lyrics)
        {
            return;
        }

        if (_manual != 0 && now - _scrolledAt > TimeSpan.FromSeconds(8))
        {
            _manual = 0;
            SetNeedsDraw();
        }

        var index = LrcParser.IndexAt(lyrics.Lines, elapsed + TimeSpan.FromSeconds(_offset));
        if (index != _lastLine)
        {
            _lastLine = index;
            SetNeedsDraw();
        }
    }

    /// <summary>Lyrics keys: up and down, Page Up and Page Down scroll, <c>[</c> and <c>]</c> shift the timing by half a second. True when the key was used.</summary>
    public bool HandleLyricsKey(Key key)
    {
        if (key == Key.CursorUp || key == Key.CursorDown)
        {
            ScrollLyrics(key == Key.CursorDown ? 1 : -1);
            return true;
        }

        if (key == Key.PageUp || key == Key.PageDown)
        {
            ScrollLyrics((key == Key.PageDown ? 1 : -1) * Math.Max(1, _bodyHeight - 1));
            return true;
        }

        if (key.AsRune.Value is '[' or ']' && _lyrics is { IsSynced: true })
        {
            // Positive offset: the lyrics come earlier (the LRC [offset:] convention).
            _offset = Math.Round(_offset + (key.AsRune.Value == ']' ? 0.5 : -0.5), 1);
            _lastLine = -2;
            SetNeedsDraw();
            return true;
        }

        return false;
    }

    private void ScrollLyrics(int rows)
    {
        if (_lyrics is null)
        {
            return;
        }

        _manual += rows;
        _scrolledAt = DateTime.UtcNow;
        SetNeedsDraw();
    }

    protected override bool OnKeyDown(Key key)
    {
        // Volume, seek and the like stay with the main window; this view only closes.
        if (key == Key.Esc)
        {
            CloseRequested?.Invoke();
            return true;
        }

        return base.OnKeyDown(key);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown) || mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            ScrollLyrics(mouse.Flags.HasFlag(MouseFlags.WheeledDown) ? 3 : -3);
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && mouse.Position is { } p)
        {
            // A click no longer closes the view (Esc does): it presses a control or switches the time to a countdown.
            if (_buttons.FirstOrDefault(b => b.Y == p.Y && p.X >= b.X0 && p.X < b.X1) is { Run: { } run })
            {
                run();
            }
            else if (Player is { Current.Station: null } seekable && _trackBar.X1 > _trackBar.X0 && p.Y == _trackBar.Y && p.X >= _trackBar.X0 && p.X < _trackBar.X1)
            {
                seekable.SeekTo((p.X - _trackBar.X0 + 0.5) / (_trackBar.X1 - _trackBar.X0));
            }
            else if (_totals.Any(t => t.Y == p.Y && p.X >= t.X0 && p.X < t.X1))
            {
                _countdown = !_countdown;
                SetNeedsDraw();
            }

            return true;
        }

        return base.OnMouseEvent(mouse);
    }
}
