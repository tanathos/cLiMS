using System.Diagnostics;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Clims.Tui.Ui;

/// <summary>Base for owner-drawn views: clears to the theme background and offers clipped text helpers.</summary>
public abstract class PaintView : View
{
    protected PaintView()
    {
        CanFocus = false;
        if (TracksHover)
        {
            s_hoverViews.Add(new WeakReference<PaintView>(this));
        }
    }

    protected int W => Viewport.Width;

    protected int H => Viewport.Height;

    /// <summary>Background the view is cleared to before <see cref="Paint"/>.</summary>
    protected virtual Attribute Base => Theme.Normal;

    private readonly List<SixelPlacement> _sixels = new();
    private readonly HashSet<CoverKey> _wantedCovers = new();
    private bool _hadCovers;

    /// <summary>
    /// True when repainting overwrites the cells under a sixel image, so it must be sent again (scrolling lists).
    /// A view whose cover cells never change between frames (the footer) returns false to avoid flicker.
    /// </summary>
    protected virtual bool ResendSixelsOnPaint => true;

    /// <summary>
    /// True for scrolling views with covers: cells under a cover that has not moved are not rewritten (so it neither
    /// flickers nor needs resending), and the cells of a cover that moved away are blanked. Nothing but the cover
    /// may be drawn inside the cover's cells.
    /// </summary>
    protected virtual bool StableSixels => false;

    private bool _fullClear = true;
    private int _lastW = -1;
    private int _lastH = -1;

    /// <summary>Forget where covers were (the view was hidden, or its layout changed): the next frame clears everything.</summary>
    public void ResetCovers() => _fullClear = true;

    /// <summary>Columns on the left that the per-frame clear leaves alone (a still sixel there must not be overwritten, or it is erased).</summary>
    protected virtual int ClearFromX => 0;

    /// <summary>Rows at the top that scrolling content must not draw into (a pinned heading): covers stay below them.</summary>
    protected virtual int ClipTop => 0;

    /// <summary>Cells holding a sixel that has not moved (a view that is not <see cref="StableSixels"/>): the per-frame clear skips them.</summary>
    protected virtual System.Drawing.Rectangle? StillCells => null;

    /// <summary>Terminal.Gui would clear the whole viewport (and erase a still sixel) before painting; the clear is ours.</summary>
    protected override bool OnClearingViewport() => StableSixels || ClearFromX > 0 || StillCells is not null || base.OnClearingViewport();

    private readonly record struct Rect(int X, int Y, int Cols, int Rows);

    private Dictionary<string, Rect> _blanked = new();
    private Dictionary<string, Rect> _blankedNow = new();

    /// <summary>Cell-drawn covers and placeholders of this frame (partly visible covers): the cleanup of moved sixels must not blank them.</summary>
    private readonly List<Rect> _drawnCovers = new();

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var started = Stopwatch.GetTimestamp();
        if (_fullClear || W != _lastW || H != _lastH)
        {
            _blanked.Clear();
            _fullClear = false;
            (_lastW, _lastH) = (W, H);
        }

        Use(Base);
        var still = StillCells;
        for (var y = 0; y < H; y++)
        {
            var from = Math.Clamp(ClearFromX, 0, W);
            var spans = StableSixels
                ? _blanked.Values.Where(r => y >= r.Y && y < r.Y + r.Rows).Select(Guarded).OrderBy(r => r.X).ToList()
                : null;
            if (still is { } keep && y >= keep.Y && y < keep.Y + keep.Height)
            {
                spans = (spans ?? new List<Rect>()).Append(new Rect(keep.X, keep.Y, keep.Width, keep.Height)).OrderBy(r => r.X).ToList();
            }

            if (spans is not { Count: > 0 })
            {
                Move(from, y);
                AddStr(new string(' ', W - from));
                continue;
            }

            // Skip the cells of covers that are still where they were: writing them would erase the image.
            var x = from;
            foreach (var r in spans)
            {
                if (r.X > x)
                {
                    Move(x, y);
                    AddStr(new string(' ', Math.Min(r.X, W) - x));
                }

                x = Math.Max(x, r.X + r.Cols);
            }

            if (x < W)
            {
                Move(x, y);
                AddStr(new string(' ', W - x));
            }
        }

        _sixels.Clear();
        _blankedNow.Clear();
        _drawnCovers.Clear();
        _wantedCovers.Clear();
        _probes.Clear();
        try
        {
            Paint();
        }
        catch (Exception ex)
        {
            // A bad frame must not take the whole app down (resize can hand views odd sizes): log and carry on.
            CrashLog.Write(ex);
        }

        if (StableSixels)
        {
            BlankMovedCovers();
        }

        (_blanked, _blankedNow) = (_blankedNow, _blanked);
        SixelCovers.Publish(this, _sixels, ResendSixelsOnPaint && !StableSixels);

        // Tell the cover source what this view shows now, so it fetches those and cancels the ones scrolled away.
        // Views that draw no covers do not register, which keeps the cover source's bookkeeping small.
        if (_wantedCovers.Count > 0 || _hadCovers)
        {
            CoverArt.Source.Want(this, _wantedCovers.ToArray());
            _hadCovers = _wantedCovers.Count > 0;
        }


        PerfStats.Record(GetType().Name, Stopwatch.GetElapsedTime(started));
        return true;
    }

    /// <summary>True while a modal view is open: every view that can be dimmed draws its colours at half brightness.</summary>
    public static bool Dimmed { get; set; }

    /// <summary>Screen area of an open popup: a cover touching it is drawn in cells, because a sixel would be painted over the popup.</summary>
    public static System.Drawing.Rectangle? SixelHole { get; set; }

    private static bool InHole(System.Drawing.Point screen, int cols, int rows) =>
        SixelHole is { } hole && hole.IntersectsWith(new System.Drawing.Rectangle(screen.X, screen.Y, cols, rows));

    /// <summary>False for the modal view itself.</summary>
    protected virtual bool Dimmable => true;

    private void Use(Attribute attr)
    {
        if (Dimmed && Dimmable)
        {
            attr = new Attribute(Shade(attr.Foreground), Shade(attr.Background), attr.Style);
        }

        SetAttribute(attr);
    }

    private static Color Shade(Color c) => new(c.R / 2, c.G / 2, c.B / 2, 255);

    /// <summary>Asks for a cover without drawing it (a better version of one that is drawn).</summary>
    protected void WantCover(CoverKey key) => _wantedCovers.Add(key);

    protected abstract void Paint();

    /// <summary>Draws a one-column scrollbar on the right edge. Nothing is drawn when everything fits.</summary>
    protected void PaintScrollbar(int total, int offset, int visible)
    {
        if (total <= visible || H < 1 || W < 1)
        {
            return;
        }

        var (thumbStart, thumbLength) = Thumb(total, offset, visible, H);
        var track = new Attribute(Theme.Border, Theme.Bg);
        var thumb = new Attribute(Theme.Thumb, Theme.Bg);
        for (var y = 0; y < H; y++)
        {
            var onThumb = y >= thumbStart && y < thumbStart + thumbLength;
            Put(W - 1, y, onThumb ? "█" : "│", onThumb ? thumb : track);
        }
    }

    private static (int Start, int Length) Thumb(int total, int offset, int visible, int height)
    {
        var length = Math.Clamp(height * visible / total, 1, height);
        var range = Math.Max(1, total - visible);
        return (Math.Clamp(offset * (height - length) / range, 0, height - length), length);
    }

    /// <summary>True for views that highlight what is under the pointer: they are redrawn when it moves, and know it in <see cref="Pointer"/>.</summary>
    protected virtual bool TracksHover => false;

    /// <summary>Where the pointer is inside this view (view coordinates), or null when it is elsewhere. Kept up to date only when <see cref="TracksHover"/>.</summary>
    protected System.Drawing.Point? Pointer { get; private set; }

    /// <summary>True when the pointer is on row <paramref name="y"/>, in columns <paramref name="x0"/> (inclusive) to <paramref name="x1"/> (exclusive).</summary>
    protected bool Over(int x0, int x1, int y)
    {
        var over = IsOver(Pointer, x0, x1, y);
        _probes.Add((x0, x1, y, over));
        return over;
    }

    private static bool IsOver(System.Drawing.Point? pointer, int x0, int x1, int y) => pointer is { } p && p.Y == y && p.X >= x0 && p.X < x1;

    /// <summary>The areas asked about with <see cref="Over"/> while painting, and the answers given: a pointer move that changes none of them needs no repaint.</summary>
    private readonly List<(int X0, int X1, int Y, bool Over)> _probes = new();

    /// <summary>True when moving the pointer to <paramref name="pointer"/> would paint something different (a view that never asks with <see cref="Over"/> is always repainted).</summary>
    private bool HoverChanged(System.Drawing.Point? pointer)
    {
        if (_probes.Count == 0)
        {
            return true;
        }

        foreach (var (x0, x1, y, was) in _probes)
        {
            if (IsOver(pointer, x0, x1, y) != was)
            {
                return true;
            }
        }

        return false;
    }

    private static readonly List<WeakReference<PaintView>> s_hoverViews = new();

    /// <summary>
    /// Tells every hover-tracking view where the pointer is, in screen cells (null: outside the window). Fed from the application's
    /// mouse event, which also reports plain movement; the views themselves are not sent movement without a button.
    /// </summary>
    public static void PointerAt(System.Drawing.Point? screen)
    {
        foreach (var weak in s_hoverViews.ToArray())
        {
            if (!weak.TryGetTarget(out var view))
            {
                s_hoverViews.Remove(weak);
                continue;
            }

            System.Drawing.Point? local = null;
            if (screen is { } s && view.ShownOnScreen && !(Dimmed && view.Dimmable))
            {
                var frame = view.FrameToScreen();
                if (frame.Contains(s))
                {
                    local = new System.Drawing.Point(s.X - frame.X, s.Y - frame.Y);
                }
            }

            if (local != view.Pointer)
            {
                var changed = view.HoverChanged(local);
                view.Pointer = local;
                if (changed)
                {
                    view.OnPointerMoved();
                }
            }
        }
    }

    private bool ShownOnScreen
    {
        get
        {
            for (View? v = this; v is not null; v = v.SuperView)
            {
                if (!v.Visible)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>The pointer moved (or left). The default redraws the view; a view with covers can decide for itself.</summary>
    protected virtual void OnPointerMoved() => SetNeedsDraw();

    private bool _barDrag;
    private static PaintView? s_pressedOn;

    /// <summary>
    /// True for a click whose button went down on another view (a drag that ended here, for example a scrollbar drag
    /// released over a row): the terminal reports it here, but it is not a click on this view and must not select anything.
    /// Call first in OnMouseEvent.
    /// </summary>
    protected bool StrayClick(Mouse mouse)
    {
        var flags = mouse.Flags;
        if (flags.HasFlag(MouseFlags.LeftButtonPressed) && !flags.HasFlag(MouseFlags.PositionReport))
        {
            s_pressedOn = this;
            return false;
        }

        if (flags.HasFlag(MouseFlags.LeftButtonClicked) || flags.HasFlag(MouseFlags.LeftButtonDoubleClicked))
        {
            var stray = s_pressedOn is not null && s_pressedOn != this;
            s_pressedOn = null;
            return stray;
        }

        return false;
    }

    /// <summary>
    /// Mouse handling of the scrollbar column: a press on it (or a click) moves the thumb there, and the drag that follows
    /// keeps moving it wherever the pointer goes. The release that ends the drag is consumed, so it never reaches the
    /// rows as a click. Returns true when the event was a scrollbar one; <paramref name="setOffset"/> gets the row of the pointer.
    /// </summary>
    protected bool ScrollbarMouse(Mouse mouse, bool scrollable, Action<int> setOffset)
    {
        var flags = mouse.Flags;
        var press = flags.HasFlag(MouseFlags.LeftButtonPressed);
        var fresh = press && !flags.HasFlag(MouseFlags.PositionReport);
        var clicked = flags.HasFlag(MouseFlags.LeftButtonClicked) || flags.HasFlag(MouseFlags.LeftButtonDoubleClicked);
        if (fresh)
        {
            _barDrag = false;
        }

        // The terminal ends a drag with a Released event and then a Clicked one at the same place: both are the drag's.
        if (_barDrag)
        {
            if (mouse.Position is { } at && press)
            {
                setOffset(at.Y);
            }

            _barDrag = !clicked;
            return true;
        }

        if (fresh && scrollable && mouse.Position is { } p && p.X >= W - 1)
        {
            setOffset(p.Y);
            _barDrag = true;
            return true;
        }

        return false;
    }

    /// <summary>Offset that puts the scrollbar thumb's centre at row y (click-to-jump).</summary>
    protected int OffsetFromScrollbar(int y, int total, int visible)
    {
        var length = Thumb(total, 0, visible, H).Length;
        var travel = Math.Max(1, H - length);
        var thumbTop = Math.Clamp(y - length / 2, 0, travel);
        return Math.Clamp(thumbTop * (total - visible) / travel, 0, Math.Max(0, total - visible));
    }

    /// <summary>Writes text at (x, y), clipped to the view width. Returns the x after the text.</summary>
    protected int Put(int x, int y, string text, Attribute attr)
    {
        if (y < 0 || y >= H || x >= W || text.Length == 0)
        {
            return x;
        }

        if (x < 0)
        {
            if (-x >= text.Length)
            {
                return x + text.Length;
            }

            text = DropSplitSurrogates(text[-x..]);
            x = 0;
        }

        if (x + text.Length > W)
        {
            text = DropSplitSurrogates(text[..(W - x)]);
        }

        Use(attr);
        Move(x, y);
        AddStr(text);
        return x + text.Length;
    }

    /// <summary>Draws a cover in the style chosen for <paramref name="place"/>; cells outside the view are skipped so it scrolls like text.</summary>
    protected void DrawCover(int x, int y, int cols, int rows, CoverKey key, CoverPlace place)
    {
        _wantedCovers.Add(key);
        var image = CoverArt.Source.TryGet(key);
        if (image is null)
        {
            DrawPlaceholder(x, y, cols, rows, loading: key.ArtworkId is not null);
            return;
        }

        if (CoverArt.UsesSixel(place) && DrawSixel(image, x, y, cols, rows))
        {
            return;
        }

        var cellMode = CoverArt.CellMode(place);
        _drawnCovers.Add(new Rect(x, y, cols, rows));
        for (var r = 0; r < rows; r++)
        {
            var cy = y + r;
            if (cy < ClipTop || cy >= H)
            {
                continue;
            }

            for (var c = 0; c < cols; c++)
            {
                var cx = x + c;
                if (cx < 0 || cx >= W)
                {
                    continue;
                }

                var (glyph, fg, bg) = CoverArt.Glyph(image, c, r, cols, rows, cellMode);
                Use(new Attribute(fg, bg));
                Move(cx, cy);
                AddStr(glyph);
            }
        }
    }

    /// <summary>
    /// Shows <paramref name="image"/> as a sixel in the given cells. False (nothing drawn) when a sixel cannot be used: no
    /// terminal support, the image is not fully inside the view (a sixel cannot be clipped), or a modal view or popup is open.
    /// </summary>
    protected bool DrawSixel(CoverImage image, int x, int y, int cols, int rows)
    {
        var inside = x >= 0 && y >= ClipTop && x + cols <= W && y + rows <= H;
        if (!inside || !SixelCovers.Available || (Dimmed && Dimmable) || (Dimmable && InHole(ViewportToScreen(new System.Drawing.Point(x, y)), cols, rows)))
        {
            return false;
        }

        var blankKey = $"{x},{y},{cols},{rows}|{image.Label}";
        _blankedNow[blankKey] = new Rect(x, y, cols, rows);
        if ((ResendSixelsOnPaint && !StableSixels) || !_blanked.ContainsKey(blankKey))
        {
            FillBlank(x, y, cols, rows);
        }

        _sixels.Add(new SixelPlacement(image, cols, rows, ViewportToScreen(new System.Drawing.Point(x, y))));
        return true;
    }

    /// <summary>
    /// Blanks the cells and marks them as drawn this frame, for content that takes the place of a sixel (or of a placeholder):
    /// the image under them is overwritten, and the clean-up of moved sixels leaves what is drawn there alone.
    /// </summary>
    protected void Reclaim(int x, int y, int cols, int rows)
    {
        _drawnCovers.Add(new Rect(x, y, cols, rows));
        FillBlank(x, y, cols, rows);
    }

    /// <summary>A dim block with a note in the middle: shown while a cover is being fetched, or when an album has none.</summary>
    private void DrawPlaceholder(int x, int y, int cols, int rows, bool loading)
    {
        _drawnCovers.Add(new Rect(x, y, cols, rows));
        var attr = new Attribute(loading ? Theme.FgDim : Theme.Faint, Theme.BgAlt);
        for (var r = 0; r < rows; r++)
        {
            FillRow(y + r, x, x + cols, attr);
        }

        Put(x + cols / 2, y + rows / 2, loading ? "…" : "♪", attr);
    }

    /// <summary>Covers that were there last frame and are gone or moved: blank their cells, except where a cover is now.</summary>
    private void BlankMovedCovers()
    {
        foreach (var (key, old) in _blanked)
        {
            if (_blankedNow.ContainsKey(key))
            {
                continue;
            }

            for (var r = 0; r < old.Rows; r++)
            {
                var cy = old.Y + r;
                var c = 0;
                while (c < old.Cols)
                {
                    var start = c;
                    while (c < old.Cols && !UnderNewCover(old.X + c, cy))
                    {
                        c++;
                    }

                    if (c > start)
                    {
                        FillRow(cy, old.X + start, old.X + c, Theme.Normal);
                    }
                    else
                    {
                        c++;
                    }
                }
            }
        }
    }

    /// <summary>The cover plus one blank cell on each side: rewriting the cell next to a sixel can disturb its edge column.</summary>
    private Rect Guarded(Rect r) => new(Math.Max(ClearFromX, r.X - 1), r.Y, r.Cols + (r.X > ClearFromX ? 2 : 1), r.Rows);

    private bool UnderNewCover(int x, int y) =>
        _blankedNow.Values.Concat(_drawnCovers).Any(n => x >= n.X && x < n.X + n.Cols && y >= n.Y && y < n.Y + n.Rows);

    private void FillBlank(int x, int y, int cols, int rows)
    {
        for (var r = 0; r < rows; r++)
        {
            FillRow(y + r, x, x + cols, Theme.Normal);
        }
    }

    protected void FillRow(int y, int x0, int x1, Attribute attr)
    {
        if (y < 0 || y >= H)
        {
            return;
        }

        x0 = Math.Max(0, x0);
        x1 = Math.Min(W, x1);
        if (x1 <= x0)
        {
            return;
        }

        Use(attr);
        Move(x0, y);
        AddStr(new string(' ', x1 - x0));
    }

    protected static string Clip(string s, int width) =>
        width <= 0 ? "" : s.Length <= width ? s : width == 1 ? "…" : DropSplitSurrogates(s[..(width - 1)]) + "…";

    /// <summary>
    /// Slicing by UTF-16 index can cut an emoji in half; Terminal.Gui throws on a lone surrogate
    /// (this crashed the app when the window was narrowed over the header's speaker emoji).
    /// </summary>
    private static string DropSplitSurrogates(string s)
    {
        if (s.Length > 0 && char.IsLowSurrogate(s[0]))
        {
            s = s[1..];
        }

        return s.Length > 0 && char.IsHighSurrogate(s[^1]) ? s[..^1] : s;
    }

    protected static string Fmt(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
}
