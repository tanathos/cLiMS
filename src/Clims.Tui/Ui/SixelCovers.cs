using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;

namespace Clims.Tui.Ui;

/// <summary>
/// Real-pixel covers through Terminal.Gui's sixel queue. Each view publishes the placements it drew in its last
/// frame; the shared queue is rebuilt from all owners. Encoded sixel data is cached per cover and size.
/// </summary>
public static class SixelCovers
{
    private static readonly Dictionary<object, Dictionary<string, Entry>> Owners = new();
    private static readonly Dictionary<(CoverImage, int, int, bool), string> Encoded = new();

    private const int SettleFrames = 4;
    private static int _nextId;

    private sealed class Entry(SixelToRender item)
    {
        public SixelToRender Item { get; } = item;

        public int Settle { get; set; }
    }

    public static IDriver? Driver { get; set; }

    public static bool Available => Driver?.SixelSupport is { IsSupported: true };

    /// <summary>Pixels per terminal cell as reported by the driver (Windows Terminal: 10x20).</summary>
    public static Size CellPixels => Driver?.SixelSupport?.Resolution ?? new Size(10, 20);

    /// <summary>
    /// Replaces what <paramref name="owner"/> shows. Unchanged placements keep their queue entry and are only
    /// re-sent when <paramref name="resend"/> is set (needed when the cells under the image were repainted).
    /// </summary>
    public static void Publish(object owner, IReadOnlyList<SixelPlacement> placements, bool resend)
    {
        if (!Available)
        {
            return;
        }

        Owners.TryGetValue(owner, out var previous);
        var next = new Dictionary<string, Entry>();
        foreach (var placement in placements)
        {
            if (previous is not null && previous.TryGetValue(placement.Key, out var existing))
            {
                // A still image is sent again for a few frames after it appears or the queue is rebuilt: the cells
                // under it are rewritten in the first frames, which would otherwise erase it. After that it stays put.
                if (resend || existing.Settle > 0)
                {
                    existing.Item.IsDirty = true;
                    existing.Settle = Math.Max(0, existing.Settle - 1);
                }

                next[placement.Key] = existing;
                continue;
            }

            next[placement.Key] = new Entry(new SixelToRender
            {
                SixelData = Encode(placement),
                ScreenPosition = placement.Screen,
                Id = placement.Key + "#" + ++_nextId,
                IsDirty = true,
            }) { Settle = SettleFrames };
        }

        var changed = previous is null || !previous.Keys.SequenceEqual(next.Keys);
        Owners[owner] = next;
        if (changed)
        {
            Rebuild();
        }
    }

    /// <summary>
    /// Sends every image again. A terminal drops the images it shows when its window is resized (and some re-flow what is
    /// left), and nothing in the app changes at that moment, so nothing would put them back.
    /// </summary>
    public static void Refresh()
    {
        foreach (var entry in Owners.Values.SelectMany(o => o.Values))
        {
            entry.Settle = SettleFrames;
            entry.Item.IsDirty = true;
        }
    }

    /// <summary>Forgets every placement (the window that owned them is gone, for example when switching server).</summary>
    public static void ReleaseAll()
    {
        Owners.Clear();
        Driver?.GetSixels().Clear();
    }

    /// <summary>Removes everything an owner showed (it was hidden or scrolled away).</summary>
    public static void Release(object owner)
    {
        (owner as PaintView)?.ResetCovers();
        if (Owners.Remove(owner))
        {
            Rebuild();
        }
    }

    private static string Encode(SixelPlacement p)
    {
        var cell = CellPixels;
        var (w, h) = (p.Cols * cell.Width, p.Rows * cell.Height);

        // A sixel that ends on the last screen row makes the terminal scroll; the encoder can drop one band to avoid it.
        var bottom = Driver is not null && p.Screen.Y + p.Rows >= Driver.Screen.Height;
        if (!Encoded.TryGetValue((p.Image, w, h, bottom), out var data))
        {
            data = new SixelEncoder { AvoidBottomScroll = bottom }.EncodeSixel(p.Image.Bitmap(w, h));
            Encoded[(p.Image, w, h, bottom)] = data;
        }

        return data;
    }

    private static void Rebuild()
    {
        if (Driver is null)
        {
            return;
        }

        var queue = Driver.GetSixels();
        queue.Clear();
        foreach (var item in Owners.Values.SelectMany(o => o.Values).Select(e => { e.Settle = SettleFrames; e.Item.IsDirty = true; return e.Item; }))
        {

            queue.Enqueue(item);
        }
    }
}
