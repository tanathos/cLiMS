using Clims.Catalog;
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
        Put(x, H - 1, "Esc or click to close", dim);
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
        var y0 = top + Math.Max(0, (rows - 9) / 2);
        Put(x, y0, Clip(artist, width), new Attribute(Theme.Accent, Theme.Bg));
        Put(x, y0 + 1, Clip(album.Title, width), new Attribute(Theme.Fg, Theme.Bg, TextStyle.Bold));
        Put(x, y0 + 2, new string('─', Math.Min(width, 40)), frame);

        const int labelW = 10;
        var label = new Attribute(Theme.FgDim, Theme.Bg);
        var value = new Attribute(Theme.Fg, Theme.Bg);
        var line = y0 + 4;
        Put(x, line, "Genre", label);
        Put(x + labelW, line, Clip(genres.Length > 0 ? genres : "unknown", width - labelW), new Attribute(Theme.Fg, Theme.Bg, TextStyle.Italic));
        line += 2;
        Put(x, line, "Runtime", label);
        Put(x + labelW, line, runtime, value);
        line += 2;
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
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
        {
            CloseRequested?.Invoke();
            return true;
        }

        return base.OnMouseEvent(mouse);
    }
}
