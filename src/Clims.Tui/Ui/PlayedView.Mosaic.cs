using Clims.Catalog;
using Clims.Tui.Settings;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>
/// The mosaic layout of the album lists of <see cref="PlayedView"/>: covers only, the more listens the larger, the most
/// listened first. The bottom line names the album under the pointer, else the selected one.
/// </summary>
public sealed partial class PlayedView
{
    /// <summary>Rows the wheel scrolls: one unit of the mosaic, so that the smallest covers stay whole at the top.</summary>
    private const int WheelRows = MosaicLayout.UnitRows + MosaicLayout.GapRows;

    /// <summary>Width of the frame around the selected cover, in pixels of a Sixel image.</summary>
    private const int FramePixels = 6;

    private MosaicLayout? _mosaic;
    private (IReadOnlyList<AlbumListen> List, int Width, int Rows) _mosaicFor;
    private int _hovered = -1;

    private bool OnMosaic => CoverArt.PlayedLayout == PlayedLayout.Mosaic && _section is Section.Week or Section.Month;

    protected override bool TracksHover => true;

    protected override int ClipBottom => OnMosaic ? 1 : 0;

    /// <summary>The mosaic of the list on show, packed again when the list or the size of the view changed.</summary>
    private MosaicLayout Mosaic()
    {
        var key = (Albums, Math.Max(1, W - 1), ListRows);
        if (_mosaic is null || !ReferenceEquals(key.Albums, _mosaicFor.List) || key.Item2 != _mosaicFor.Width || key.ListRows != _mosaicFor.Rows)
        {
            _mosaicFor = key;
            _mosaic = MosaicLayout.Pack(Albums.Select(a => MosaicLayout.SpanFor(a.InWindow)).ToList(), key.Item2, key.ListRows);
        }

        return _mosaic;
    }

    /// <summary>The column the mosaic starts at: it is centred.</summary>
    private int MosaicLeft => Math.Max(0, (W - 1 - Mosaic().Width) / 2);

    /// <summary>The tile at a cell of the view, or -1.</summary>
    private int TileAt(int x, int y) =>
        y >= HeadRows && y < HeadRows + ListRows && x < W - 1 ? Mosaic().At(x - MosaicLeft, y - HeadRows + _offset) : -1;

    /// <summary>The F7 key or the settings changed the layout: the same album stays selected, in its new place.</summary>
    public void LayoutChanged()
    {
        var chosen = _section is Section.Week or Section.Month && _selected < Count ? ShownBefore()[_selected] : null;
        _shownLayout = CoverArt.PlayedLayout;
        _offset = _section is Section.Week or Section.Month ? 0 : _offset;
        ResetCovers();
        if (chosen is not null)
        {
            _selected = Math.Max(0, Albums.ToList().IndexOf(chosen));
            Move(0);
        }

        SetNeedsDraw();
    }

    private void PaintMosaic()
    {
        var layout = Mosaic();
        var left = MosaicLeft;
        for (var i = 0; i < layout.Tiles.Count; i++)
        {
            var tile = layout.Tiles[i];
            var top = HeadRows + tile.Y - _offset;
            if (top + tile.Rows > HeadRows && top < HeadRows + ListRows)
            {
                PaintTile(Albums[i], left + tile.X, top, tile, i == _selected);
            }
        }

        _hovered = Pointer is { } at ? TileAt(at.X, at.Y) : -1;
        PaintStatus(_hovered >= 0 ? _hovered : _selected);
    }

    private void PaintTile(AlbumListen listen, int x, int y, MosaicTile tile, bool selected)
    {
        var album = Resolve(listen.AlbumId, listen.Artist, listen.Album);
        var key = new CoverKey(album?.ArtworkId, listen.AlbumId);
        var frame = HasFocus ? Theme.Accent : Theme.FgDim;
        // A tile wider than the list-size picture asks for a sharper one (only a Sixel shows the difference) and has the
        // list-size one stand in until it arrives.
        CoverImage? image = null;
        if (CoverArt.UsesSixel(CoverPlace.Library) && tile.Cols * SixelCovers.CellPixels.Width > LmsCoverSource.NormalSize)
        {
            var sharp = key with { Resolution = CoverResolution.Medium };
            WantCover(sharp);
            image = CoverArt.Source.TryGet(sharp);
        }

        if (image is null && (image = CoverArt.Source.TryGet(key)) is not null)
        {
            WantCover(key);
        }

        if (image is null)
        {
            // No picture (yet): the name stands in for it, so the tile can still be told apart.
            DrawCover(x, y, tile.Cols, tile.Rows, key, CoverPlace.Library);
            var style = new Terminal.Gui.Drawing.Attribute(Theme.Fg, Theme.BgAlt);
            PutInTile(x, y + 1, tile, listen.Album, style);
            PutInTile(x, y + 2, tile, listen.Artist, style);
            if (selected)
            {
                var edge = new Terminal.Gui.Drawing.Attribute(frame, Theme.BgAlt);
                PutInTile(x, y, tile, new string('▀', tile.Cols), edge);
                PutInTile(x, y + tile.Rows - 1, tile, new string('▄', tile.Cols), edge);
            }

            return;
        }

        if (selected)
        {
            // The frame is part of the picture: writing the cells next to a Sixel image would erase its edge.
            var whole = y >= HeadRows && y + tile.Rows <= HeadRows + ListRows;
            var share = whole && CoverArt.UsesSixel(CoverPlace.Library)
                ? (double)FramePixels / (tile.Cols * SixelCovers.CellPixels.Width)
                : 1.0 / tile.Cols;
            image = image.Framed(frame, share);
        }

        DrawImage(image, x, y, tile.Cols, tile.Rows, CoverPlace.Library);
    }

    /// <summary>A line of text centred in a tile, left out where the tile is scrolled under the status line.</summary>
    private void PutInTile(int x, int y, MosaicTile tile, string text, Terminal.Gui.Drawing.Attribute attr)
    {
        if (y >= HeadRows && y < HeadRows + ListRows)
        {
            var line = Clip(text, tile.Cols);
            Put(x + (tile.Cols - line.Length) / 2, y, line, attr);
        }
    }

    private void PaintStatus(int index)
    {
        var y = H - 1;
        FillRow(y, 0, W, Theme.Normal);
        Claim(0, y, W, 1);
        if (index >= 0 && index < Count)
        {
            var listen = Albums[index];
            Put(1, y, Clip($"{listen.Album} · {listen.Artist} · {Listens(listen)} · {Ago(listen.LastListen, _now)}", Math.Max(1, W - 3)), Theme.Dim);
        }
    }

    /// <summary>Only a move to another cover changes what is drawn (the name on the bottom line).</summary>
    protected override void OnPointerMoved()
    {
        var hovered = OnMosaic && _message is null && Pointer is { } at ? TileAt(at.X, at.Y) : -1;
        if (hovered != _hovered)
        {
            _hovered = hovered;
            SetNeedsDraw();
        }
    }

    /// <summary>Selects a tile and scrolls it into view.</summary>
    private void SelectTile(int index)
    {
        if (Count == 0)
        {
            return;
        }

        _selected = Math.Clamp(index, 0, Count - 1);
        var tile = Mosaic().Tiles[_selected];
        var rows = ListRows;
        if (tile.Y < _offset)
        {
            _offset = tile.Y;
        }
        else if (tile.Bottom > _offset + rows)
        {
            _offset = tile.Bottom - rows;
        }

        SetNeedsDraw();
    }

    /// <summary>Arrows go to the neighbouring cover, page keys a screen further, Home and End to the first and last.</summary>
    private bool MosaicKey(Key key)
    {
        if (Count == 0 || _selected >= Count)
        {
            return false;
        }

        var layout = Mosaic();
        var tile = layout.Tiles[_selected];
        int? next = key.KeyCode switch
        {
            KeyCode.CursorRight => layout.Neighbor(_selected, 1, 0),
            KeyCode.CursorLeft => layout.Neighbor(_selected, -1, 0),
            KeyCode.CursorDown => layout.Neighbor(_selected, 0, 1),
            KeyCode.CursorUp => layout.Neighbor(_selected, 0, -1),
            KeyCode.PageDown => layout.Nearest(tile.X, tile.Y + ListRows),
            KeyCode.PageUp => layout.Nearest(tile.X, tile.Y - ListRows),
            KeyCode.Home => 0,
            KeyCode.End => Count - 1,
            _ => null,
        };
        if (next is not { } index)
        {
            return false;
        }

        SelectTile(index);
        return true;
    }
}
