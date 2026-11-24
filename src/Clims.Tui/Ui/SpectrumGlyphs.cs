namespace Clims.Tui.Ui;

/// <summary>Turns band levels (0 to 1) into the two rows of characters of a <see cref="SpectrumStyle"/>.</summary>
public static class SpectrumGlyphs
{
    private const string Blocks = " ▁▂▃▄▅▆▇█";

    // Braille dots of one cell column, bottom to top (left column: dots 7 3 2 1, right column: dots 8 6 5 4).
    private static readonly int[] LeftDots = [0x40, 0x04, 0x02, 0x01];
    private static readonly int[] RightDots = [0x80, 0x20, 0x10, 0x08];

    /// <param name="style">The look.</param>
    /// <param name="bands">Level of each band, 0 to 1; they are stretched over <paramref name="width"/> columns.</param>
    /// <param name="width">Cells per row.</param>
    public static SpectrumFrame Render(SpectrumStyle style, float[] bands, int width)
    {
        var frame = new SpectrumFrame(width);
        if (width <= 0 || bands.Length == 0)
        {
            return frame;
        }

        switch (style)
        {
            case SpectrumStyle.Braille:
                Braille(frame, bands, width);
                break;
            case SpectrumStyle.Mirror:
                Mirror(frame, bands, width);
                break;
            default:
                Bars(frame, bands, width, gaps: style == SpectrumStyle.Slim, hot: style == SpectrumStyle.Heat);
                break;
        }

        return frame;
    }

    private static void Bars(SpectrumFrame frame, float[] bands, int width, bool gaps, bool hot)
    {
        for (var i = 0; i < width; i++)
        {
            if (gaps && i % 2 == 1)
            {
                continue;
            }

            var level = (int)Math.Round(At(bands, i, width) * 16);
            frame.Bottom[i] = Blocks[Math.Clamp(level, 0, 8)];
            frame.Top[i] = level > 8 ? Blocks[Math.Min(level - 8, 8)] : ' ';
            if (hot && level > 8)
            {
                frame.TopKind[i] = SpectrumFrame.Hot;
            }
        }
    }

    private static void Mirror(SpectrumFrame frame, float[] bands, int width)
    {
        for (var i = 0; i < width; i++)
        {
            var level = Math.Clamp((int)Math.Round(At(bands, i, width) * 8), 0, 8);
            frame.Top[i] = Blocks[level];
            if (level > 0)
            {
                // The lower half hangs from the middle line: the block that is empty below is drawn with the colours swapped.
                frame.Bottom[i] = Blocks[8 - level];
                frame.BottomKind[i] = SpectrumFrame.Inverted;
            }
        }
    }

    private static void Braille(SpectrumFrame frame, float[] bands, int width)
    {
        var columns = width * 2;
        var height = new int[columns];
        for (var j = 0; j < columns; j++)
        {
            height[j] = Math.Clamp((int)Math.Round(At(bands, j, columns) * 8), 0, 8);
        }

        for (var i = 0; i < width; i++)
        {
            int top = 0, bottom = 0;
            for (var d = 0; d < 4; d++)
            {
                if (height[2 * i] > d) { bottom |= LeftDots[d]; }
                if (height[2 * i + 1] > d) { bottom |= RightDots[d]; }
                if (height[2 * i] > d + 4) { top |= LeftDots[d]; }
                if (height[2 * i + 1] > d + 4) { top |= RightDots[d]; }
            }

            frame.Bottom[i] = bottom == 0 ? ' ' : (char)(0x2800 + bottom);
            frame.Top[i] = top == 0 ? ' ' : (char)(0x2800 + top);
        }
    }

    /// <summary>The level at column <paramref name="i"/> of <paramref name="count"/>, interpolated between neighbouring bands.</summary>
    private static float At(float[] bands, int i, int count)
    {
        var at = i * (bands.Length - 1) / (float)Math.Max(1, count - 1);
        var lo = (int)at;
        var hi = Math.Min(bands.Length - 1, lo + 1);
        return bands[lo] + (bands[hi] - bands[lo]) * (at - lo);
    }
}
