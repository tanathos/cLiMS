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

    private static readonly string[] PitchClasses = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>
    /// The notes of the chromagram over <paramref name="width"/> cells, as many as fit with a bar of two cells for each and a cell
    /// between them, from C3 up (the Cs carry their octave: C3, C4 …). With less room than 12 notes need, the octaves are folded into
    /// the 12 pitch classes instead, each with a wider bar. The note that is clearly strongest is drawn in the warning colour.
    /// </summary>
    /// <param name="notes">Level of each note from <see cref="Clims.Visualizer.Chromagram.FirstMidi"/> up, 0 to 1.</param>
    public static SpectrumFrame Chroma(float[] notes, int width)
    {
        const int pitch = 3;
        var count = Math.Min(notes.Length, (width + 1) / pitch);
        if (count >= 12)
        {
            var names = new string[count];
            for (var i = 0; i < count; i++)
            {
                var midi = Clims.Visualizer.Chromagram.FirstMidi + i;
                names[i] = midi % 12 == 0 ? "C" + (midi / 12 - 1) : PitchClasses[midi % 12];
            }

            return Layout(notes.AsSpan(0, count).ToArray(), names, width, pitch);
        }

        var folded = new float[12];
        for (var i = 0; i < notes.Length; i++)
        {
            var pitchClass = (Clims.Visualizer.Chromagram.FirstMidi + i) % 12;
            folded[pitchClass] = Math.Max(folded[pitchClass], notes[i]);
        }

        return Layout(folded, PitchClasses, width, width / 12);
    }

    /// <summary>A name on the upper row and a bar of 8 steps on the lower one for each level, <paramref name="pitch"/> cells apart (the last of them a gap).</summary>
    private static SpectrumFrame Layout(float[] levels, string[] names, int width, int pitch)
    {
        var frame = new SpectrumFrame(width);
        if (width <= 0 || levels.Length == 0 || pitch < 1)
        {
            return frame;
        }

        var strongest = 0;
        for (var n = 1; n < levels.Length; n++)
        {
            strongest = levels[n] > levels[strongest] ? n : strongest;
        }

        var gap = pitch >= 2 ? 1 : 0;
        var cells = pitch - gap;
        var left = (width - (pitch * levels.Length - gap)) / 2;
        for (var n = 0; n < levels.Length; n++)
        {
            var start = left + n * pitch;
            var kind = n == strongest && levels[n] > 0.3f ? SpectrumFrame.Hot : SpectrumFrame.Normal;
            var level = Math.Clamp((int)Math.Round(levels[n] * 8), 0, 8);
            for (var i = 0; i < cells; i++)
            {
                frame.Bottom[start + i] = Blocks[level];
                frame.BottomKind[start + i] = kind;
            }

            var name = names[n];
            if (name.Length <= cells)
            {
                var at = start + (cells - name.Length) / 2;
                for (var i = 0; i < name.Length; i++)
                {
                    frame.Top[at + i] = name[i];
                    frame.TopKind[at + i] = kind;
                }
            }
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
