namespace Clims.Lyrics.Tests;

public class LrcParserTests
{
    [Fact]
    public void Reads_stamps_and_words_in_time_order()
    {
        var lines = LrcParser.Parse("[ar:Someone]\n[00:12.50]second\n[00:01.00]first\r\n[01:02]third");

        Assert.Equal(["first", "second", "third"], lines.Select(l => l.Text));
        Assert.Equal(TimeSpan.FromSeconds(1), lines[0].Time);
        Assert.Equal(TimeSpan.FromSeconds(12.5), lines[1].Time);
        Assert.Equal(TimeSpan.FromSeconds(62), lines[2].Time);
    }

    [Fact]
    public void A_line_with_several_stamps_is_repeated()
    {
        var lines = LrcParser.Parse("[00:10.00][00:50.00]chorus");

        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal("chorus", l.Text));
        Assert.Equal(TimeSpan.FromSeconds(50), lines[1].Time);
    }

    [Fact]
    public void The_offset_tag_moves_every_line_and_never_goes_below_zero()
    {
        var lines = LrcParser.Parse("[offset:+2000]\n[00:01.00]a\n[00:10.00]b");

        Assert.Equal(TimeSpan.Zero, lines[0].Time);
        Assert.Equal(TimeSpan.FromSeconds(8), lines[1].Time);
    }

    [Fact]
    public void Word_stamps_are_dropped_and_empty_lines_kept_as_gaps()
    {
        var lines = LrcParser.Parse("[00:01.00]<00:01.00>Hel<00:01.40>lo\n[00:05.00]");

        Assert.Equal("Hello", lines[0].Text);
        Assert.Equal("", lines[1].Text);
    }

    [Fact]
    public void Text_without_stamps_is_ignored()
    {
        Assert.Empty(LrcParser.Parse("just words\nno stamps"));
    }

    [Fact]
    public void IndexAt_finds_the_line_being_sung()
    {
        var lines = LrcParser.Parse("[00:05.00]a\n[00:10.00]b\n[00:20.00]c");

        Assert.Equal(-1, LrcParser.IndexAt(lines, TimeSpan.FromSeconds(1)));
        Assert.Equal(0, LrcParser.IndexAt(lines, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, LrcParser.IndexAt(lines, TimeSpan.FromSeconds(19.9)));
        Assert.Equal(2, LrcParser.IndexAt(lines, TimeSpan.FromMinutes(5)));
    }
}
