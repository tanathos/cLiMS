using System.Text;
using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>
/// Turns a picture into sixel data with a palette built by median cut and Floyd-Steinberg dithering. The encoder that comes with
/// Terminal.Gui merges similar colours and so flattens a cover into a handful of tones; this one keeps the whole 256 colours the
/// terminal allows, placed where the cover needs them.
/// </summary>
public static class SixelPicture
{
    private const int Bins = 1 << 15;     // the colours counted at 5 bits per channel
    private const int KeyBits = 6;        // nearest-colour answers are remembered at 6 bits per channel

    /// <summary>The sixel sequence for <paramref name="pixels"/> (indexed [x, y]).</summary>
    /// <param name="avoidBottomScroll">Drop the last band of six rows: a sixel that ends on the last screen row makes the terminal scroll.</param>
    /// <param name="maxColors">At most this many palette colours (at most 256).</param>
    public static string Encode(Color[,] pixels, bool avoidBottomScroll = false, int maxColors = 256)
    {
        var w = pixels.GetLength(0);
        var h = pixels.GetLength(1);
        if (avoidBottomScroll && h > 6)
        {
            h -= 6;
        }

        if (w == 0 || h == 0)
        {
            return string.Empty;
        }

        var palette = BuildPalette(pixels, w, h, Math.Clamp(maxColors, 2, 256));
        var indices = Dither(pixels, w, h, palette);
        return Write(indices, w, h, palette);
    }

    /// <summary>Median cut over a histogram of the colours: split the most populated box along its widest channel until there are enough boxes.</summary>
    private static (int R, int G, int B)[] BuildPalette(Color[,] pixels, int w, int h, int maxColors)
    {
        var count = new int[Bins];
        var sumR = new long[Bins];
        var sumG = new long[Bins];
        var sumB = new long[Bins];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = pixels[x, y];
                var bin = ((c.R >> 3) << 10) | ((c.G >> 3) << 5) | (c.B >> 3);
                count[bin]++;
                sumR[bin] += c.R;
                sumG[bin] += c.G;
                sumB[bin] += c.B;
            }
        }

        var bins = new List<int>();
        for (var i = 0; i < Bins; i++)
        {
            if (count[i] > 0)
            {
                bins.Add(i);
            }
        }

        var items = bins.ToArray();
        var boxes = new List<(int Start, int End)> { (0, items.Length) };
        while (boxes.Count < maxColors)
        {
            // The splittable box with the most pixels.
            var best = -1;
            long bestPixels = 0;
            for (var b = 0; b < boxes.Count; b++)
            {
                var (s, e) = boxes[b];
                if (e - s < 2)
                {
                    continue;
                }

                long pop = 0;
                for (var i = s; i < e; i++)
                {
                    pop += count[items[i]];
                }

                if (pop > bestPixels)
                {
                    (best, bestPixels) = (b, pop);
                }
            }

            if (best < 0)
            {
                break;
            }

            var (start, end) = boxes[best];
            int minR = 31, maxR = 0, minG = 31, maxG = 0, minB = 31, maxB = 0;
            for (var i = start; i < end; i++)
            {
                var bin = items[i];
                int r = bin >> 10, g = (bin >> 5) & 31, bl = bin & 31;
                (minR, maxR) = (Math.Min(minR, r), Math.Max(maxR, r));
                (minG, maxG) = (Math.Min(minG, g), Math.Max(maxG, g));
                (minB, maxB) = (Math.Min(minB, bl), Math.Max(maxB, bl));
            }

            // Green is what the eye separates best, blue least.
            var spanR = (maxR - minR) * 3;
            var spanG = (maxG - minG) * 4;
            var spanB = (maxB - minB) * 2;
            Func<int, int> key = spanG >= spanR && spanG >= spanB
                ? bin => (bin >> 5) & 31
                : spanR >= spanB ? bin => bin >> 10 : bin => bin & 31;
            Array.Sort(items, start, end - start, Comparer<int>.Create((a, b) => key(a).CompareTo(key(b))));

            var half = bestPixels / 2;
            long run = 0;
            var cut = start + 1;
            for (var i = start; i < end - 1; i++)
            {
                run += count[items[i]];
                cut = i + 1;
                if (run >= half)
                {
                    break;
                }
            }

            boxes[best] = (start, cut);
            boxes.Add((cut, end));
        }

        var palette = new (int, int, int)[boxes.Count];
        for (var b = 0; b < boxes.Count; b++)
        {
            long n = 0, r = 0, g = 0, bl = 0;
            for (var i = boxes[b].Start; i < boxes[b].End; i++)
            {
                var bin = items[i];
                n += count[bin];
                r += sumR[bin];
                g += sumG[bin];
                bl += sumB[bin];
            }

            palette[b] = ((int)(r / n), (int)(g / n), (int)(bl / n));
        }

        return palette;
    }

    /// <summary>Maps every pixel to the palette, carrying the error of each choice over to the pixels next to it.</summary>
    private static byte[] Dither(Color[,] pixels, int w, int h, (int R, int G, int B)[] palette)
    {
        var result = new byte[w * h];
        var cache = new short[1 << (3 * KeyBits)];
        Array.Fill(cache, (short)-1);
        const int shift = 8 - KeyBits;
        var current = new float[(w + 2) * 3];
        var next = new float[(w + 2) * 3];
        for (var y = 0; y < h; y++)
        {
            Array.Clear(next);
            for (var x = 0; x < w; x++)
            {
                var c = pixels[x, y];
                var o = (x + 1) * 3;
                var r = Math.Clamp(c.R + current[o], 0, 255);
                var g = Math.Clamp(c.G + current[o + 1], 0, 255);
                var b = Math.Clamp(c.B + current[o + 2], 0, 255);
                var key = (((int)r >> shift) << (2 * KeyBits)) | (((int)g >> shift) << KeyBits) | ((int)b >> shift);
                var index = cache[key];
                if (index < 0)
                {
                    index = Nearest(palette, (int)r, (int)g, (int)b);
                    cache[key] = index;
                }

                result[y * w + x] = (byte)index;
                var p = palette[index];
                float er = r - p.R, eg = g - p.G, eb = b - p.B;
                current[o + 3] += er * 7 / 16;
                current[o + 4] += eg * 7 / 16;
                current[o + 5] += eb * 7 / 16;
                next[o - 3] += er * 3 / 16;
                next[o - 2] += eg * 3 / 16;
                next[o - 1] += eb * 3 / 16;
                next[o] += er * 5 / 16;
                next[o + 1] += eg * 5 / 16;
                next[o + 2] += eb * 5 / 16;
                next[o + 3] += er / 16;
                next[o + 4] += eg / 16;
                next[o + 5] += eb / 16;
            }

            (current, next) = (next, current);
        }

        return result;
    }

    private static short Nearest((int R, int G, int B)[] palette, int r, int g, int b)
    {
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < palette.Length; i++)
        {
            int dr = r - palette[i].R, dg = g - palette[i].G, db = b - palette[i].B;
            var d = 3 * dr * dr + 4 * dg * dg + 2 * db * db;
            if (d < bestDistance)
            {
                (best, bestDistance) = (i, d);
            }
        }

        return (short)best;
    }

    private static string Write(byte[] indices, int w, int h, (int R, int G, int B)[] palette)
    {
        var sb = new StringBuilder(w * h / 4);
        sb.Append("\u001bP0;0;0q\"1;1;").Append(w).Append(';').Append(h);
        for (var i = 0; i < palette.Length; i++)
        {
            sb.Append('#').Append(i).Append(";2;").Append((palette[i].R * 100 + 127) / 255).Append(';').Append((palette[i].G * 100 + 127) / 255).Append(';').Append((palette[i].B * 100 + 127) / 255);
        }

        var bits = new byte[palette.Length][];
        var used = new List<int>();
        var seen = new int[palette.Length];   // the band (by its top row + 1) a colour was last used in
        for (var top = 0; top < h; top += 6)
        {
            used.Clear();
            for (var row = 0; row < 6 && top + row < h; row++)
            {
                for (var x = 0; x < w; x++)
                {
                    var c = indices[(top + row) * w + x];
                    if (bits[c] is null)
                    {
                        bits[c] = new byte[w];
                    }

                    if (seen[c] != top + 1)
                    {
                        seen[c] = top + 1;
                        used.Add(c);
                    }

                    bits[c][x] |= (byte)(1 << row);
                }
            }

            if (top > 0)
            {
                sb.Append('-');
            }

            for (var u = 0; u < used.Count; u++)
            {
                var column = bits[used[u]];
                var last = w - 1;
                while (last >= 0 && column[last] == 0)
                {
                    last--;
                }

                if (u > 0)
                {
                    sb.Append('$');
                }

                sb.Append('#').Append(used[u]);
                for (var x = 0; x <= last;)
                {
                    var value = column[x];
                    var run = 1;
                    while (x + run <= last && column[x + run] == value)
                    {
                        run++;
                    }

                    var glyph = (char)(63 + value);
                    if (run > 3)
                    {
                        sb.Append('!').Append(run).Append(glyph);
                    }
                    else
                    {
                        sb.Append(glyph, run);
                    }

                    x += run;
                }

                Array.Clear(column);
            }
        }

        return sb.Append("\u001b\\").ToString();
    }
}
