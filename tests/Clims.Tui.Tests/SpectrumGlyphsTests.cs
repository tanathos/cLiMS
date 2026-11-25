using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public class SpectrumGlyphsTests
{
    private static float[] Flat(float level) => Enumerable.Repeat(level, 32).ToArray();

    [Fact]
    public void Bars_fill_both_rows_at_full_level_and_nothing_at_zero()
    {
        var full = SpectrumGlyphs.Render(SpectrumStyle.Bars, Flat(1), 10);
        var none = SpectrumGlyphs.Render(SpectrumStyle.Bars, Flat(0), 10);

        Assert.All(full.Top, c => Assert.Equal('█', c));
        Assert.All(full.Bottom, c => Assert.Equal('█', c));
        Assert.All(none.Top, c => Assert.Equal(' ', c));
        Assert.All(none.Bottom, c => Assert.Equal(' ', c));
    }

    [Fact]
    public void Half_level_fills_only_the_lower_row()
    {
        var frame = SpectrumGlyphs.Render(SpectrumStyle.Bars, Flat(0.5f), 6);

        Assert.All(frame.Bottom, c => Assert.Equal('█', c));
        Assert.All(frame.Top, c => Assert.Equal(' ', c));
    }

    [Fact]
    public void Slim_leaves_every_other_column_empty()
    {
        var frame = SpectrumGlyphs.Render(SpectrumStyle.Slim, Flat(1), 8);

        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(i % 2 == 0 ? '█' : ' ', frame.Bottom[i]);
            Assert.Equal(i % 2 == 0 ? '█' : ' ', frame.Top[i]);
        }
    }

    [Fact]
    public void Mirror_hangs_the_lower_half_from_the_middle_line()
    {
        var quarter = SpectrumGlyphs.Render(SpectrumStyle.Mirror, Flat(0.5f), 4); // 4 of 8 levels each way
        var full = SpectrumGlyphs.Render(SpectrumStyle.Mirror, Flat(1), 4);
        var none = SpectrumGlyphs.Render(SpectrumStyle.Mirror, Flat(0), 4);

        Assert.Equal('▄', quarter.Top[0]);
        Assert.Equal(SpectrumFrame.Normal, quarter.TopKind[0]);
        Assert.Equal('▄', quarter.Bottom[0]); // the empty lower half of the cell, drawn with the colours swapped
        Assert.Equal(SpectrumFrame.Inverted, quarter.BottomKind[0]);
        Assert.Equal('█', full.Top[0]);
        Assert.Equal(' ', full.Bottom[0]); // a blank cell with swapped colours is a solid block
        Assert.Equal(SpectrumFrame.Inverted, full.BottomKind[0]);
        Assert.Equal(' ', none.Top[0]);
        Assert.Equal(' ', none.Bottom[0]);
        Assert.Equal(SpectrumFrame.Normal, none.BottomKind[0]);
    }

    [Fact]
    public void Braille_has_eight_levels_over_the_two_rows()
    {
        var full = SpectrumGlyphs.Render(SpectrumStyle.Braille, Flat(1), 5);
        var half = SpectrumGlyphs.Render(SpectrumStyle.Braille, Flat(0.5f), 5);
        var none = SpectrumGlyphs.Render(SpectrumStyle.Braille, Flat(0), 5);

        Assert.All(full.Top, c => Assert.Equal('⣿', c));
        Assert.All(full.Bottom, c => Assert.Equal('⣿', c));
        Assert.All(half.Bottom, c => Assert.Equal('⣿', c));
        Assert.All(half.Top, c => Assert.Equal(' ', c));
        Assert.All(none.Bottom, c => Assert.Equal(' ', c));
    }

    [Fact]
    public void Braille_columns_are_independent()
    {
        // Two bands over one cell: the left dot column is full height, the right one empty.
        var frame = SpectrumGlyphs.Render(SpectrumStyle.Braille, [1f, 0f], 1);

        Assert.Equal((char)(0x2800 + 0x01 + 0x02 + 0x04 + 0x40), frame.Top[0]);
        Assert.Equal((char)(0x2800 + 0x01 + 0x02 + 0x04 + 0x40), frame.Bottom[0]);
    }

    [Fact]
    public void Heat_marks_the_upper_row_only_where_the_bar_reaches_it()
    {
        var loud = SpectrumGlyphs.Render(SpectrumStyle.Heat, Flat(1), 4);
        var quiet = SpectrumGlyphs.Render(SpectrumStyle.Heat, Flat(0.3f), 4);

        Assert.All(loud.TopKind, k => Assert.Equal(SpectrumFrame.Hot, k));
        Assert.All(quiet.TopKind, k => Assert.Equal(SpectrumFrame.Normal, k));
        Assert.All(loud.BottomKind, k => Assert.Equal(SpectrumFrame.Normal, k));
    }

    [Fact]
    public void The_chromagram_shows_as_many_notes_as_fit_with_bars_two_cells_wide()
    {
        var notes = new float[60];
        notes[21] = 1f; // A4
        notes[12] = 0.4f; // C4

        var frame = SpectrumGlyphs.Chroma(notes, 90); // 30 notes of 3 cells: C3 up to F5

        Assert.Equal(90, frame.Top.Length);
        Assert.Equal("C3", new string(frame.Top, 0, 2)); // the Cs carry their octave
        Assert.Equal("C#", new string(frame.Top, 3, 2));
        Assert.Equal('D', frame.Top[6]);
        Assert.Equal("C4", new string(frame.Top, 36, 2));
        Assert.Equal("██", new string(frame.Bottom, 63, 2)); // A4
        Assert.Equal(SpectrumFrame.Hot, frame.BottomKind[63]);
        Assert.Equal(' ', frame.Bottom[65]); // the cell between two bars
        Assert.Equal('▃', frame.Bottom[36]); // C4 at 0.4 of 8 steps
        Assert.Equal(SpectrumFrame.Normal, frame.BottomKind[36]);
    }

    [Fact]
    public void The_chromagram_with_more_room_than_five_octaves_is_centred()
    {
        var frame = SpectrumGlyphs.Chroma(new float[60], 200);

        Assert.Equal(200, frame.Top.Length);
        Assert.Equal(' ', frame.Top[0]);
        Assert.Contains("C3", new string(frame.Top));
        Assert.Contains("C7", new string(frame.Top));
        Assert.DoesNotContain("C8", new string(frame.Top));
    }

    [Fact]
    public void The_chromagram_folds_the_octaves_when_twelve_notes_do_not_fit()
    {
        var notes = new float[60];
        notes[9] = 0.5f; // A3
        notes[21] = 1f; // A4: the loudest A counts

        var frame = SpectrumGlyphs.Chroma(notes, 48); // room for 16 notes of 3 cells: not folded
        Assert.Contains("C3", new string(frame.Top)); // notes with their octaves, not folded

        var narrow = SpectrumGlyphs.Chroma(notes, 34); // 11 notes of 3 cells: folded, 2 cells each
        Assert.Equal('A', narrow.Top[23]); // pitch 2, bar of 1, 12 notes centred: A is the tenth, starting at 5 + 9 * 2
        Assert.Equal('█', narrow.Bottom[23]);
    }

    [Fact]
    public void The_chromagram_of_silence_and_in_a_tiny_space_is_safe()
    {
        var silent = SpectrumGlyphs.Chroma(new float[60], 120);
        Assert.All(silent.Bottom, c => Assert.Equal(' ', c));
        Assert.All(silent.BottomKind, k => Assert.Equal(SpectrumFrame.Normal, k));

        Assert.Equal(12, SpectrumGlyphs.Chroma(new float[60], 12).Top.Length); // one cell each: bars only, no room for names
        Assert.Empty(SpectrumGlyphs.Chroma(new float[60], 0).Top);
        Assert.Equal(5, SpectrumGlyphs.Chroma(new float[60], 5).Top.Length);
    }

    [Fact]
    public void Rows_have_the_width_asked_for_and_empty_input_is_safe()
    {
        Assert.Equal(37, SpectrumGlyphs.Render(SpectrumStyle.Bars, Flat(0.7f), 37).Top.Length);
        Assert.Empty(SpectrumGlyphs.Render(SpectrumStyle.Bars, [], 0).Top);
        Assert.All(SpectrumGlyphs.Render(SpectrumStyle.Mirror, [], 5).Top, c => Assert.Equal(' ', c));
    }
}
