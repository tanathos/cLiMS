using System.Diagnostics;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;

namespace Clims.Tui.Ui;

/// <summary>Base for owner-drawn views: clears to the theme background and offers clipped text helpers.</summary>
public abstract class PaintView : View
{
    protected PaintView()
    {
        CanFocus = false;
    }

    protected int W => Viewport.Width;

    protected int H => Viewport.Height;

    /// <summary>Background the view is cleared to before <see cref="Paint"/>.</summary>
    protected virtual Attribute Base => Theme.Normal;

    private readonly List<SixelPlacement> _sixels = new();

    /// <summary>
    /// True when repainting overwrites the cells under a sixel image, so it must be sent again (scrolling lists).
    /// A view whose cover cells never change between frames (the footer) returns false to avoid flicker.
    /// </summary>
    protected virtual bool ResendSixelsOnPaint => true;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var started = Stopwatch.GetTimestamp();
        SetAttribute(Base);
        for (var y = 0; y < H; y++)
        {
            Move(0, y);
            AddStr(new string(' ', W));
        }

        _sixels.Clear();
        try
        {
            Paint();
        }
        catch (Exception ex)
        {
            // A bad frame must not take the whole app down (resize can hand views odd sizes): log and carry on.
            CrashLog.Write(ex);
        }

        SixelCovers.Publish(this, _sixels, ResendSixelsOnPaint);
        PerfStats.Record(GetType().Name, Stopwatch.GetElapsedTime(started));
        return true;
    }

    protected abstract void Paint();

    /// <summary>Draws a one-column scrollbar on the right edge. Nothing is drawn when everything fits.</summary>
    protected void PaintScrollbar(int total, int offset, int visible)
    {
        if (total <= visible || H < 1 || W < 1)
        {
            return;
        }

        var (thumbStart, thumbLength) = Thumb(total, offset, visible, H);
        var track = new Attribute(Theme.Rgb(52, 57, 70), Theme.Bg);
        var thumb = new Attribute(Theme.Rgb(150, 158, 176), Theme.Bg);
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

        SetAttribute(attr);
        Move(x, y);
        AddStr(text);
        return x + text.Length;
    }

    /// <summary>Draws a cover in the current <see cref="CoverArt.Mode"/>; cells outside the view are skipped so it scrolls like text.</summary>
    protected void DrawCover(int x, int y, int cols, int rows, int seed)
    {
        // A sixel cannot be clipped, so only covers fully inside the view use it; the rest fall back to cells.
        var inside = x >= 0 && y >= 0 && x + cols <= W && y + rows <= H;
        if (inside && CoverArt.UseSixel)
        {
            FillBlank(x, y, cols, rows);
            _sixels.Add(new SixelPlacement(CoverArt.ImageFor(seed)!, cols, rows, ViewportToScreen(new System.Drawing.Point(x, y))));
            return;
        }

        for (var r = 0; r < rows; r++)
        {
            var cy = y + r;
            if (cy < 0 || cy >= H)
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

                var (glyph, fg, bg) = CoverArt.Glyph(seed, c, r, cols, rows);
                SetAttribute(new Attribute(fg, bg));
                Move(cx, cy);
                AddStr(glyph);
            }
        }
    }

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

        SetAttribute(attr);
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
