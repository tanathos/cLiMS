namespace Clims.Tui.Ui;

/// <summary>The two-row text renderings of a waveform (peaks 0..1, drawn upwards from the bottom row).</summary>
public static class WaveformGlyphs
{
    /// <summary>Block characters by height, from empty to full.</summary>
    public const string Levels = " ▁▂▃▄▅▆▇█";

    // Braille dot bits by column (left, right) and row (top to bottom).
    private static readonly int[,] Dots = { { 0x01, 0x02, 0x04, 0x40 }, { 0x08, 0x10, 0x20, 0x80 } };

    /// <summary>One block character per column: 16 levels over the two rows.</summary>
    public static (string Top, string Bottom) Blocks(IReadOnlyList<float> peaks, int width)
    {
        var top = new char[width];
        var bottom = new char[width];
        for (var i = 0; i < width; i++)
        {
            var level = (int)(Peak(peaks, i, width) * 16);
            bottom[i] = Levels[Math.Min(level, 8)];
            top[i] = level > 8 ? Levels[Math.Min(level - 8, 8)] : ' ';
        }

        return (new string(top), new string(bottom));
    }

    /// <summary>Braille: two peaks per column, 8 dot rows over the two rows.</summary>
    public static (string Top, string Bottom) Braille(IReadOnlyList<float> peaks, int width)
    {
        var top = new char[width];
        var bottom = new char[width];
        for (var i = 0; i < width; i++)
        {
            int upper = 0, lower = 0;
            for (var side = 0; side < 2; side++)
            {
                var level = Math.Clamp((int)Math.Round(Peak(peaks, 2 * i + side, 2 * width) * 8), 0, 8);
                for (var row = 8 - level; row < 8; row++)
                {
                    if (row < 4)
                    {
                        upper |= Dots[side, row];
                    }
                    else
                    {
                        lower |= Dots[side, row - 4];
                    }
                }
            }

            top[i] = (char)(0x2800 + upper);
            bottom[i] = (char)(0x2800 + lower);
        }

        return (new string(top), new string(bottom));
    }

    /// <summary>The loudest peak among those that fall in slot <paramref name="index"/> of <paramref name="slots"/>.</summary>
    public static float Peak(IReadOnlyList<float> peaks, int index, int slots)
    {
        if (peaks.Count == 0 || slots <= 0)
        {
            return 0;
        }

        var from = Math.Min(peaks.Count - 1, index * peaks.Count / slots);
        var to = Math.Min(peaks.Count, Math.Max(from + 1, (index + 1) * peaks.Count / slots));
        var peak = 0f;
        for (var k = from; k < to; k++)
        {
            peak = Math.Max(peak, peaks[k]);
        }

        return peak;
    }
}
