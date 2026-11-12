using Clims.Tui.Ui;
using Terminal.Gui.Drawing;

namespace Clims.Tui.Tests;

public class WaveformGlyphsTests
{
    [Fact]
    public void Blocks_fill_from_the_bottom_and_spill_into_the_top_row()
    {
        var (top, bottom) = WaveformGlyphs.Blocks([0f, 0.5f, 0.99f], 3);
        Assert.Equal(" ", top[..1]);
        Assert.Equal('█', bottom[1]);
        Assert.Equal(' ', top[1]);
        Assert.Equal('█', bottom[2]);
        Assert.NotEqual(' ', top[2]);
    }

    [Fact]
    public void Braille_is_empty_for_silence_and_full_for_a_full_peak()
    {
        Assert.Equal(("⠀", "⠀"), WaveformGlyphs.Braille([0f, 0f], 1));
        Assert.Equal(("⣿", "⣿"), WaveformGlyphs.Braille([1f, 1f], 1));
    }

    [Fact]
    public void Braille_has_two_peaks_per_cell()
    {
        // Left half silent, right half full: only the right-hand dots (column 2) are set.
        var (top, bottom) = WaveformGlyphs.Braille([0f, 1f], 1);
        Assert.Equal((char)(0x2800 + 0x08 + 0x10 + 0x20 + 0x80), top[0]);
        Assert.Equal((char)(0x2800 + 0x08 + 0x10 + 0x20 + 0x80), bottom[0]);
    }

    [Fact]
    public void Image_bars_grow_from_the_bottom_and_the_played_part_has_its_own_colour()
    {
        var done = Color.Parse("#ff0000");
        var todo = Color.Parse("#00ff00");
        var back = Color.Parse("#000000");
        var pixels = WaveformImage.Render([1f, 1f], 8, 8, 4, playedPixels: 4, done, todo, back);
        Assert.Equal(done, pixels[0, 0]);
        Assert.Equal(done, pixels[0, 7]);
        Assert.Equal(back, pixels[3, 0]);   // the gap between bars
        Assert.Equal(todo, pixels[4, 4]);
        var half = WaveformImage.Render([0.5f], 4, 8, 4, 0, done, todo, back);
        Assert.Equal(back, half[0, 3]);
        Assert.Equal(todo, half[0, 4]);
    }
}
