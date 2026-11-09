using System.Globalization;
using Clims.Catalog;
using Clims.Lyrics;
using Clims.Tui.Data;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>
/// Full-screen view of the playing album, opened by a click on the player's cover: the cover on the left, and on the
/// right the artist, album title, genres, runtime and file format. Any key or click closes it.
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
        var frame = new Attribute(Theme.Rgb(52, 57, 70), Theme.Bg);
        var dim = Theme.Dim;
        // As tall as the panel; the width follows from the shape of a cell, so the cover stays square.
        var cell = SixelCovers.CellPixels;
        var rows = H;
        var cols = Math.Max(2, rows * cell.Height / Math.Max(1, cell.Width));
        var maxCols = Math.Max(8, W * 55 / 100);
        if (cols > maxCols)
        {
            cols = maxCols;
            rows = Math.Max(4, cols * cell.Width / Math.Max(1, cell.Height));
        }

        const int left = 0;
        var top = (H - rows) / 2;
        var big = new CoverKey(album.ArtworkId, album.Id, Big: true);
        var small = new CoverKey(album.ArtworkId, album.Id);
        // The large picture takes a moment to arrive: show the list-size one meanwhile.
        WantCover(big);
        DrawCover(left, top, cols, rows, CoverArt.Source.TryGet(big) is null ? small : big);

        var x = left + cols + 5;
        var width = W - x - 3;
        var hint = "Esc or click to close";
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
        var runtime = summary is { TrackCount: > 0 } ? $"{(int)Math.Round(summary.Duration.TotalMinutes)} min" : "unknown";
        var urls = (_catalog?.TracksOf(album.Id).Select(t => t.Url) ?? []).Append(now.Track.Url);
        var (formats, lossless) = FormatsOf(urls);

        var artist = album.ArtistName.Length > 0 ? album.ArtistName : now.Track.Artist ?? "";
        // Anchored to the top of the cover: the lyrics take the rest of the column below.
        var y0 = top + 1; // one empty line above the artist
        Put(x, y0, Clip(artist, width), new Attribute(Theme.Accent, Theme.Bg));
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

        PaintLyrics(x, y0 + 9, y0 + 10,Math.Min(top + rows, H - 2) - 1, width);
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
        if (key == Key.Esc || key == Key.Enter || key.AsRune.Value is 'q' or 'Q')
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

        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
        {
            CloseRequested?.Invoke();
            return true;
        }

        return base.OnMouseEvent(mouse);
    }
}
