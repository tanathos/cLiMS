using System.Globalization;
using Clims.Catalog;
using Clims.Info;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>
/// The "Info" dialog of an album: about 75% of the window, opened from the album's menu. On the left a picture of the artist (when
/// the provider has one; else the album's cover), on the right what the providers know about the artist and the album. The lookups
/// run when the dialog opens, one after the other, and each part appears as soon as it arrives. Esc closes it.
/// </summary>
public sealed class InfoDialog : PaintView
{
    private enum Load
    {
        Idle,
        Loading,
        Found,
        Missing,
        Failed,
    }

    private enum Kind
    {
        Title,
        Heading,
        Body,
        Dim,
        Blank,
        Link,
    }

    /// <summary>One row of the text; a row with a <paramref name="Url"/> opens it in the browser when clicked.</summary>
    private readonly record struct Line(string Text, Kind Kind, string? Url = null);

    private readonly InfoService _service;
    private readonly List<Line> _lines = new();
    private int _link = -1;
    private string? _note;
    private CancellationTokenSource? _cancel;
    private CatalogAlbum? _album;
    private Load _artistState;
    private Load _albumState;
    private ArtistInfo? _artist;
    private AlbumInfo? _albumInfo;
    private CoverImage? _photo;
    private int _linesWidth = -1;
    private int _scroll;
    private int _textRows = 1;
    private int _textX = 2;

    public InfoDialog(InfoService service)
    {
        _service = service;
        CanFocus = true;
    }

    /// <summary>The user pressed a key or clicked to close the dialog.</summary>
    public event Action? CloseRequested;

    protected override Attribute Base => new(Theme.Fg, Theme.BgAlt);

    protected override bool Dimmable => false;

    /// <summary>Starts the lookups for an album and shows what is already known (nothing yet).</summary>
    /// <param name="album">The album.</param>
    /// <param name="refresh">Ask the providers again even for what is stored.</param>
    public void Show(CatalogAlbum album, bool refresh = false)
    {
        Cancel();
        _link = -1;
        _note = refresh ? "refreshing…" : null;
        var cancel = _cancel = new CancellationTokenSource();
        _album = album;
        _artistState = Load.Loading;
        _albumState = Load.Idle;
        _artist = null;
        _albumInfo = null;
        _photo?.Dispose();
        _photo = null;
        _scroll = 0;
        _linesWidth = -1;
        ResetCovers();
        SetNeedsDraw();
        _ = LoadAsync(album, refresh, cancel.Token);
    }

    /// <summary>Stops the lookups still running (the dialog closes).</summary>
    public void Cancel()
    {
        _cancel?.Cancel();
        _cancel?.Dispose();
        _cancel = null;
    }

    private async Task LoadAsync(CatalogAlbum album, bool refresh, CancellationToken ct)
    {
        try
        {
            var query = new InfoQuery(album.ArtistName, album.Title);
            ArtistInfo? artist = null;
            var artistState = Load.Missing;
            try
            {
                artist = await _service.ArtistAsync(query, ct, refresh).ConfigureAwait(false);
                artistState = artist is null ? Load.Missing : Load.Found;
            }
            catch (HttpRequestException)
            {
                artistState = Load.Failed;
            }

            Post(ct, () =>
            {
                _artist = artist;
                _artistState = artistState;
                _albumState = Load.Loading;
                _linesWidth = -1;
            });

            if (artist is { ImageUrls.Count: > 0 })
            {
                _ = LoadPhotoAsync(artist.ImageUrls, refresh, ct);
            }

            AlbumInfo? info = null;
            var albumState = Load.Missing;
            try
            {
                info = await _service.AlbumAsync(query with { ArtistId = artist?.Id }, ct, refresh).ConfigureAwait(false);
                albumState = info is null ? Load.Missing : Load.Found;
            }
            catch (HttpRequestException)
            {
                albumState = Load.Failed;
            }

            Post(ct, () =>
            {
                _albumInfo = info;
                _albumState = albumState;
                _linesWidth = -1;
                _note = null;
            });
        }
        catch (OperationCanceledException)
        {
            // The dialog was closed.
        }
    }

    private async Task LoadPhotoAsync(IReadOnlyList<string> urls, bool refresh, CancellationToken ct)
    {
        try
        {
            foreach (var url in urls.Take(3))
            {
                byte[]? bytes;
                try
                {
                    bytes = await _service.ImageAsync(url, ct, refresh).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    continue;
                }

                if (bytes is not null && CoverImage.Decode(bytes, url) is { } image)
                {
                    Post(ct, () => _photo = image);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The dialog was closed.
        }
    }

    /// <summary>Runs <paramref name="change"/> on the UI thread (unless the dialog was closed meanwhile) and draws again.</summary>
    private void Post(CancellationToken ct, Action change)
    {
        void Run()
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            change();
            SetNeedsDraw();
        }

        if (App is { } app)
        {
            app.Invoke(Run);
        }
        else
        {
            Run();
        }
    }

    protected override void Paint()
    {
        for (var y = 0; y < H; y++)
        {
            FillRow(y, 0, W, Base);
        }

        var border = new Attribute(Theme.Border, Theme.BgAlt);
        Put(0, 0, "┌" + new string('─', Math.Max(0, W - 2)) + "┐", border);
        Put(0, H - 1, "└" + new string('─', Math.Max(0, W - 2)) + "┘", border);
        for (var y = 1; y < H - 1; y++)
        {
            Put(0, y, "│", border);
            Put(W - 1, y, "│", border);
        }

        Put(2, 0, " Info ", new Attribute(Theme.Accent, Theme.BgAlt));
        if (_album is null)
        {
            return;
        }

        var contentW = W - 4;
        var leftCols = contentW >= 70 ? Math.Clamp(contentW * 40 / 100, 20, 60) : 0;
        var textX = leftCols > 0 ? 2 + leftCols + 2 : 2;
        var textW = Math.Max(10, W - 2 - textX);
        _textRows = Math.Max(1, H - 2);
        _textX = textX;

        if (leftCols > 0)
        {
            PaintPictures(2, 1, leftCols, H - 2);
        }

        if (_linesWidth != textW)
        {
            BuildLines(textW);
            _linesWidth = textW;
            _link = -1;
        }

        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _lines.Count - _textRows));
        for (var row = 0; row < _textRows && _scroll + row < _lines.Count; row++)
        {
            var (text, kind, _) = _lines[_scroll + row];
            var chosen = _scroll + row == _link;
            Put(textX, 1 + row, text, kind switch
            {
                Kind.Title => new Attribute(Theme.Accent, Theme.BgAlt, Terminal.Gui.Drawing.TextStyle.Bold),
                Kind.Heading => new Attribute(Theme.Fg, Theme.BgAlt, Terminal.Gui.Drawing.TextStyle.Bold),
                Kind.Dim => new Attribute(Theme.FgDim, Theme.BgAlt),
                Kind.Link when chosen => new Attribute(Theme.HoverFg, Theme.HoverBg, Terminal.Gui.Drawing.TextStyle.Underline),
                Kind.Link => new Attribute(Theme.Accent, Theme.BgAlt),
                _ => new Attribute(Theme.Fg, Theme.BgAlt),
            });
        }

        var hint = _note ?? (_lines.Count > _textRows ? "Esc close  ·  ↑↓ scroll  ·  Tab/click links  ·  r refresh" : "Esc close  ·  Tab/click links  ·  r refresh");
        Put(W - 2 - hint.Length - 1, H - 1, " " + hint + " ", new Attribute(Theme.FgDim, Theme.BgAlt));
    }

    /// <summary>
    /// The artist's picture at the top of the column (its own proportions) and the album's cover under it when there is room;
    /// without a picture the cover takes the column.
    /// </summary>
    private void PaintPictures(int x, int y, int cols, int rows)
    {
        var album = _album!;
        var key = new CoverKey(album.ArtworkId, album.Id);
        var used = 0;
        if (_photo is { } photo)
        {
            var (c, r) = Fit(photo.PixelWidth, photo.PixelHeight, cols, Math.Max(3, rows * 2 / 3));
            DrawImage(photo, x, y, c, r, CoverPlace.FullScreen);
            used = r + 1;
        }

        var left = rows - used;
        var cell = SixelCovers.CellPixels;
        var coverCols = Math.Min(cols, left * cell.Height / Math.Max(1, cell.Width)); // square: a cell is about twice as tall as wide
        if (coverCols >= 8)
        {
            var (c, r) = Fit(1, 1, coverCols, left);
            DrawCover(x, y + used, c, r, key, CoverPlace.FullScreen);
        }
    }

    /// <summary>The largest block of cells of the picture's proportions (a cell is about twice as tall as wide) inside the given cells.</summary>
    private static (int Cols, int Rows) Fit(int pixelWidth, int pixelHeight, int maxCols, int maxRows)
    {
        var cell = SixelCovers.CellPixels;
        var cols = maxCols;
        var rows = (int)Math.Round(cols * (double)pixelHeight / Math.Max(1, pixelWidth) * cell.Width / Math.Max(1, cell.Height));
        if (rows > maxRows)
        {
            rows = maxRows;
            cols = (int)Math.Round(rows * (double)pixelWidth / Math.Max(1, pixelHeight) * cell.Height / Math.Max(1, cell.Width));
        }

        return (Math.Clamp(cols, 1, maxCols), Math.Max(1, rows));
    }

    private void BuildLines(int width)
    {
        _lines.Clear();
        AddArtist(width);
        _lines.Add(new Line("", Kind.Blank));
        AddAlbum(width);
        _lines.Add(new Line("", Kind.Blank));
        var sources = string.Join(", ", new[] { _artist?.Provider, _albumInfo?.Provider }
            .SelectMany(p => (p ?? "").Split(", ", StringSplitOptions.RemoveEmptyEntries))
            .Distinct()
            .Select(p => p switch { "musicbrainz" => "MusicBrainz", "wikipedia" => "Wikipedia", _ => p }));
        Add(sources.Length == 0 ? "Sources: none found yet" : "Sources: " + sources, Kind.Dim, width);
    }

    private void AddArtist(int width)
    {
        var name = _album!.ArtistName;
        switch (_artistState)
        {
            case Load.Loading:
                Add(name, Kind.Title, width);
                Add("Looking up the artist…", Kind.Dim, width);
                return;
            case Load.Failed:
                Add(name, Kind.Title, width);
                Add("The artist could not be looked up: the service did not answer. Press r to try again.", Kind.Dim, width);
                return;
            case Load.Missing:
                Add(name, Kind.Title, width);
                Add("No information found for this artist.", Kind.Dim, width);
                return;
        }

        var a = _artist!;
        Add(a.Name + (string.IsNullOrEmpty(a.Disambiguation) ? "" : $"  ({a.Disambiguation})"), Kind.Title, width);
        var facts = new List<string>();
        AddIf(facts, a.Kind);
        AddIf(facts, string.Join(", ", new[] { a.Area, a.Country }.Where(s => !string.IsNullOrEmpty(s))));
        if (!string.IsNullOrEmpty(a.Begin))
        {
            facts.Add(a.Ended ? $"{a.Begin} – {a.End ?? "?"}" : $"since {a.Begin}");
        }

        Add(string.Join("  ·  ", facts), Kind.Dim, width);
        if (a.Genres.Count > 0)
        {
            Add("Genres: " + string.Join(", ", a.Genres), Kind.Body, width);
        }

        _lines.Add(new Line("", Kind.Blank));
        Add(a.Biography ?? "No biography found.", a.Biography is null ? Kind.Dim : Kind.Body, width);
        if (a.Biography is not null && a.BiographySource == "wikipedia")
        {
            Add("Biography: Wikipedia, licensed CC BY-SA 4.0", Kind.Dim, width);
        }
        if (a.Members.Count > 0)
        {
            _lines.Add(new Line("", Kind.Blank));
            Add("Members", Kind.Heading, width);
            foreach (var m in a.Members.Take(14))
            {
                var years = m.Begin is null && m.End is null ? "" : $"  {m.Begin ?? "?"}–{(m.Current ? "" : m.End ?? "?")}";
                Add($"• {m.Name}{(m.Role is null ? "" : $" ({m.Role})")}{years}", m.Current ? Kind.Body : Kind.Dim, width);
            }
        }

        AddLinks(a.Links, width);
    }

    private void AddAlbum(int width)
    {
        var title = _album!.Title;
        switch (_albumState)
        {
            case Load.Idle or Load.Loading:
                Add(title, Kind.Title, width);
                Add("Looking up the album…", Kind.Dim, width);
                return;
            case Load.Failed:
                Add(title, Kind.Title, width);
                Add("The album could not be looked up: the service did not answer.", Kind.Dim, width);
                return;
            case Load.Missing:
                Add(title, Kind.Title, width);
                Add("No information found for this album.", Kind.Dim, width);
                return;
        }

        var a = _albumInfo!;
        Add(a.Title, Kind.Title, width);
        var facts = new List<string>();
        AddIf(facts, string.Join(" + ", new[] { a.Kind }.Concat(a.Secondary).Where(s => !string.IsNullOrEmpty(s))));
        AddIf(facts, a.FirstReleased);
        AddIf(facts, a.Country);
        if (a.TrackCount is { } tracks)
        {
            facts.Add(tracks.ToString(CultureInfo.InvariantCulture) + " tracks");
        }

        Add(string.Join("  ·  ", facts), Kind.Dim, width);
        if (a.Label is not null)
        {
            Add("Label: " + a.Label + (a.CatalogNumber is null ? "" : $"  ({a.CatalogNumber})"), Kind.Body, width);
        }

        if (a.Genres.Count > 0)
        {
            Add("Genres: " + string.Join(", ", a.Genres), Kind.Body, width);
        }

        if (a.Rating is { } rating)
        {
            Add($"Rating: {rating.ToString("0.0", CultureInfo.InvariantCulture)} / 5  ({a.Votes} votes)", Kind.Body, width);
        }

        if (!string.IsNullOrEmpty(a.Description))
        {
            _lines.Add(new Line("", Kind.Blank));
            Add(a.Description, Kind.Body, width);
            if (a.DescriptionSource == "wikipedia")
            {
                Add("Description: Wikipedia, licensed CC BY-SA 4.0", Kind.Dim, width);
            }
        }

        AddLinks(a.Links, width);
    }

    private void AddLinks(IReadOnlyList<InfoLink> links, int width)
    {
        if (links.Count == 0)
        {
            return;
        }

        _lines.Add(new Line("", Kind.Blank));
        Add("Links", Kind.Heading, width);
        foreach (var link in links.Take(8))
        {
            // One row each, cut at the edge: a wrapped address could not be clicked as one thing. The scheme is left off so that terminals that underline web addresses under the mouse leave the row alone.
            var shown = link.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? link.Url[8..] : link.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? link.Url[7..] : link.Url;
            var text = $"{link.Kind}: {shown}";
            _lines.Add(new Line(text.Length <= width ? text : text[..Math.Max(1, width - 1)] + "…", Kind.Link, link.Url));
        }
    }

    private static void AddIf(List<string> facts, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            facts.Add(value);
        }
    }

    /// <summary>Adds the text word-wrapped to the width (a word longer than the width is cut).</summary>
    private void Add(string text, Kind kind, int width)
    {
        foreach (var paragraph in text.Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var w = word;
                while (w.Length > width)
                {
                    if (line.Length > 0)
                    {
                        _lines.Add(new Line(line, kind));
                        line = "";
                    }

                    _lines.Add(new Line(w[..width], kind));
                    w = w[width..];
                }

                if (line.Length == 0)
                {
                    line = w;
                }
                else if (line.Length + 1 + w.Length <= width)
                {
                    line += " " + w;
                }
                else
                {
                    _lines.Add(new Line(line, kind));
                    line = w;
                }
            }

            _lines.Add(new Line(line, kind));
        }
    }

    private void Scroll(int rows)
    {
        _scroll = Math.Clamp(_scroll + rows, 0, Math.Max(0, _lines.Count - _textRows));
        SetNeedsDraw();
    }

    /// <summary>Selects the next (or previous) link row and scrolls to it.</summary>
    private void MoveLink(int direction)
    {
        var rows = Enumerable.Range(0, _lines.Count).Where(i => _lines[i].Url is not null).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var at = rows.IndexOf(_link);
        _link = at < 0 ? (direction > 0 ? rows[0] : rows[^1]) : rows[(at + direction + rows.Count) % rows.Count];
        if (_link < _scroll)
        {
            _scroll = _link;
        }
        else if (_link >= _scroll + _textRows)
        {
            _scroll = _link - _textRows + 1;
        }

        SetNeedsDraw();
    }

    private void OpenLink(int row)
    {
        if (row < 0 || row >= _lines.Count || _lines[row].Url is not { } url)
        {
            return;
        }

        _note = Browser.Open(url) ? "opened in the browser" : "could not open the browser";
        SetNeedsDraw();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Tab)
        {
            MoveLink(1);
            return true;
        }

        if (key == Key.Tab.WithShift)
        {
            MoveLink(-1);
            return true;
        }

        if (key == Key.Enter)
        {
            OpenLink(_link);
            return true;
        }

        if (key.AsRune.Value is 'r' or 'R' && _album is { } album)
        {
            Show(album, refresh: true);
            return true;
        }

        switch (key.KeyCode)
        {
            case KeyCode.CursorDown:
                Scroll(1);
                return true;
            case KeyCode.CursorUp:
                Scroll(-1);
                return true;
            case KeyCode.PageDown:
                Scroll(_textRows - 1);
                return true;
            case KeyCode.PageUp:
                Scroll(-(_textRows - 1));
                return true;
            case KeyCode.Home:
                Scroll(-_lines.Count);
                return true;
            case KeyCode.End:
                Scroll(_lines.Count);
                return true;
            case KeyCode.Esc:
                CloseRequested?.Invoke();
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

        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && mouse.Position is { } p && p.Y >= 1 && p.Y <= _textRows && p.X >= _textX)
        {
            var row = _scroll + p.Y - 1;
            if (row < _lines.Count && _lines[row].Url is not null)
            {
                _link = row;
                OpenLink(row);
                return true;
            }
        }

        return base.OnMouseEvent(mouse);
    }
}
