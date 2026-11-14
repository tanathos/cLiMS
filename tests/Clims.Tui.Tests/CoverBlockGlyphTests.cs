using System.Text;
using Clims.Tui.Ui;
using Terminal.Gui.Drawing;

namespace Clims.Tui.Tests;

public class CoverBlockGlyphTests
{
    [Fact]
    public void Sextant_patterns_map_to_their_characters()
    {
        Assert.Equal(" ", CoverArt.SextantGlyph(0));
        Assert.Equal("▌", CoverArt.SextantGlyph(21));
        Assert.Equal("▐", CoverArt.SextantGlyph(42));
        Assert.Equal("█", CoverArt.SextantGlyph(63));
        Assert.Equal("\U0001FB00", CoverArt.SextantGlyph(1));     // SEXTANT-1
        Assert.Equal("\U0001FB13", CoverArt.SextantGlyph(20));    // SEXTANT-35
        Assert.Equal("\U0001FB14", CoverArt.SextantGlyph(22));    // SEXTANT-235
        Assert.Equal("\U0001FB27", CoverArt.SextantGlyph(41));    // SEXTANT-146
        Assert.Equal("\U0001FB3B", CoverArt.SextantGlyph(62));    // SEXTANT-23456
    }

    [Fact]
    public void Octant_patterns_map_to_their_characters()
    {
        Assert.Equal("\U0001CD00", CoverArt.OctantGlyph(4));      // OCTANT-3, the first
        Assert.Equal("\U0001CD01", CoverArt.OctantGlyph(6));      // OCTANT-23
        Assert.Equal("\U0001CDE4", CoverArt.OctantGlyph(253));    // OCTANT-1345678
        Assert.Equal("\U0001CDE5", CoverArt.OctantGlyph(254));    // OCTANT-2345678, the last
        Assert.Equal("█", CoverArt.OctantGlyph(255));
        Assert.Equal(" ", CoverArt.OctantGlyph(0));
        Assert.Equal("▀", CoverArt.OctantGlyph(15));
        Assert.Equal("▟", CoverArt.OctantGlyph(250));
        Assert.Equal(256, Enumerable.Range(0, 256).Select(CoverArt.OctantGlyph).Distinct().Count());
    }

    [Fact]
    public void Every_block_character_takes_one_cell()
    {
        var glyphs = Enumerable.Range(0, 64).Select(CoverArt.SextantGlyph).Concat(Enumerable.Range(0, 256).Select(CoverArt.OctantGlyph));
        foreach (var glyph in glyphs)
        {
            var rune = Rune.GetRuneAt(glyph, 0);
            Assert.Equal(glyph.Length, rune.Utf16SequenceLength);
            Assert.Equal(1, Wcwidth.UnicodeCalculator.GetWidth(rune.Value));
        }
    }

    [Fact]
    public void The_retro_palettes_only_produce_their_own_colours()
    {
        var seen = new HashSet<Color>();
        for (var i = 0; i < 64; i++)
        {
            seen.Add(CoverPalette.Map(CoverMode.GameBoy, new Color(i * 4, 255 - i * 4, i * 2), i, i / 3));
        }

        Assert.InRange(seen.Count, 2, 4);
        Assert.Equal(new Color(0, 0, 0), CoverPalette.Map(CoverMode.C64, new Color(0, 0, 0), 0, 0));
        Assert.Equal(new Color(255, 255, 255), CoverPalette.Map(CoverMode.C64, new Color(255, 255, 255), 1, 1));
    }

    [Theory]
    [InlineData(CoverMode.GameBoy)]
    [InlineData(CoverMode.C64)]
    [InlineData(CoverMode.GameBoyQuadrant)]
    [InlineData(CoverMode.C64Quadrant)]
    public void Palette_cells_hold_two_palette_colours_and_a_block_character(CoverMode mode)
    {
        var pixels = new Color[64, 64];
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                pixels[x, y] = new Color(x * 4, y * 4, (x + y) * 2);
            }
        }

        var image = CoverImage.FromPixels(pixels, "palette");
        var colours = CoverPalette.Colors(mode);
        var glyphs = CoverPalette.UsesOctants(mode) ? Enumerable.Range(0, 256).Select(CoverArt.OctantGlyph).ToHashSet() : " ▘▝▀▖▌▞▛▗▚▐▜▄▙▟█".Select(c => c.ToString()).ToHashSet();
        for (var row = 0; row < 4; row++)
        {
            for (var col = 0; col < 8; col++)
            {
                var (glyph, fg, bg) = CoverArt.Glyph(image, col, row, 8, 4, mode);
                Assert.Contains(glyph, glyphs);
                Assert.Contains(fg, colours);
                Assert.Contains(bg, colours);
            }
        }
    }

    [Fact]
    public void Styles_that_need_a_special_font_or_terminal_carry_a_warning()
    {
        foreach (var style in new[] { CoverMode.Sixel, CoverMode.Chunky, CoverMode.ChunkyScanlines, CoverMode.Sextant, CoverMode.Octant, CoverMode.GameBoy, CoverMode.C64 })
        {
            Assert.False(string.IsNullOrEmpty(CoverArt.Warning(style)), style.ToString());
        }

        foreach (var style in new[] { CoverMode.HalfBlock, CoverMode.Quadrant, CoverMode.Braille, CoverMode.GameBoyQuadrant, CoverMode.C64Quadrant })
        {
            Assert.Null(CoverArt.Warning(style));
        }
    }

    [Fact]
    public void Chunky_pictures_are_made_of_square_blocks_and_scanlines_darken_a_row_of_each()
    {
        var pixels = new Color[96, 96];
        for (var y = 0; y < 96; y++)
        {
            for (var x = 0; x < 96; x++)
            {
                pixels[x, y] = new Color(x * 2, y * 2, 128);
            }
        }

        var image = CoverImage.FromPixels(pixels, "chunky");
        var plain = ChunkyPicture.Make(image, 240, 240, CoverMode.Chunky);
        Assert.Equal(plain[0, 0], plain[4, 4]);          // one block of 5 x 5 pixels
        Assert.NotEqual(plain[0, 0], plain[200, 200]);

        var scan = ChunkyPicture.Make(image, 240, 240, CoverMode.ChunkyScanlines);
        var rows = Enumerable.Range(0, 4).Select(y => scan[100, y].G).ToList();
        Assert.True(rows.Distinct().Count() > 1, "a block row is darker than the others");
    }
}
