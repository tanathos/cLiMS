using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public class LyricsLayoutTests
{
    [Fact]
    public void Long_lines_wrap_at_spaces_and_keep_their_line_number()
    {
        var rows = LyricsLayout.Rows(["one two three four", "five"], 9);

        Assert.Equal(["one two", "three", "four", "five"], rows.Select(r => r.Text));
        Assert.Equal([0, 0, 0, 1], rows.Select(r => r.Line));
    }

    [Fact]
    public void A_word_longer_than_the_width_is_cut_and_an_empty_line_stays_a_row()
    {
        var rows = LyricsLayout.Rows(["abcdefghij", "", "x"], 4);

        Assert.Equal(["abcd", "efgh", "ij", "", "x"], rows.Select(r => r.Text));
        Assert.Equal(3, LyricsLayout.FirstRowOf(rows, 1));
    }

    [Fact]
    public void The_focused_row_is_centred_and_the_view_stays_inside_the_text()
    {
        Assert.Equal(0, LyricsLayout.Top(rowCount: 100, height: 10, focusRow: 2, manual: 0));
        Assert.Equal(45, LyricsLayout.Top(100, 10, 50, 0));
        Assert.Equal(90, LyricsLayout.Top(100, 10, 99, 0));
        Assert.Equal(0, LyricsLayout.Top(5, 10, 3, 0));
    }

    [Fact]
    public void Scrolling_by_hand_moves_from_the_followed_position_and_is_clamped()
    {
        Assert.Equal(48, LyricsLayout.Top(100, 10, 50, 3));
        Assert.Equal(0, LyricsLayout.Top(100, 10, 50, -500));
        Assert.Equal(90, LyricsLayout.Top(100, 10, 50, 500));
        Assert.Equal(7, LyricsLayout.Top(100, 10, -1, 7));
    }
}
