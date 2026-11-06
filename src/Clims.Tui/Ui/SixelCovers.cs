using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;

namespace Clims.Tui.Ui;

/// <summary>Where a cover should be drawn as a Sixel image, in screen cells.</summary>
public sealed record SixelPlacement(CoverImage Image, int Cols, int Rows, Point Screen)
{
    public string Key => $"{Image.Label}|{Cols}x{Rows}@{Screen.X},{Screen.Y}";
}

/// <summary>
/// Real-pixel covers through Terminal.Gui's sixel queue. Each view publishes the placements it drew in its last
/// frame; the shared queue is rebuilt from all owners. Encoded sixel data is cached per cover and size.
/// </summary>
public static class SixelCovers
{
    private static readonly Dictionary<object, Dictionary<string, SixelToRender>> Owners = new();
    private static readonly Dictionary<(CoverImage, int, int, bool), string> Encoded = new();

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
        var next = new Dictionary<string, SixelToRender>();
        foreach (var placement in placements)
        {
            if (previous is not null && previous.TryGetValue(placement.Key, out var existing))
            {
                existing.IsDirty = resend;
                next[placement.Key] = existing;
                continue;
            }

            next[placement.Key] = new SixelToRender
            {
                SixelData = Encode(placement),
                ScreenPosition = placement.Screen,
                IsDirty = true,
            };
        }

        var changed = previous is null || !previous.Keys.SequenceEqual(next.Keys);
        Owners[owner] = next;
        if (changed)
        {
            Rebuild();
        }
    }

    /// <summary>Removes everything an owner showed (it was hidden or scrolled away).</summary>
    public static void Release(object owner)
    {
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
        foreach (var item in Owners.Values.SelectMany(o => o.Values))
        {
            queue.Enqueue(item);
        }
    }
}
