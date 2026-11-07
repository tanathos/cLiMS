using System.Diagnostics;
using Clims.Lms;

namespace Clims.Catalog.Tests;

public class HighlightTests
{
    [Fact]
    public void Marks_the_matching_fragment_of_the_original_text()
    {
        var h = TextFolding.Highlights("Pantéon Blues", new[] { "pante" });
        Assert.Equal(new[] { (0, 5) }, h);
    }

    [Fact]
    public void Marks_each_word_wherever_it_is_and_merges_touching_ranges()
    {
        Assert.Equal(new[] { (0, 4), (14, 3) }, TextFolding.Highlights("Iron Maiden - Run", new[] { "iron", "run" }));
        Assert.Equal(new[] { (0, 4) }, TextFolding.Highlights("Iron", new[] { "ir", "iron" })); // overlapping: one range
    }

    [Fact]
    public void Prefers_a_word_start_over_a_hit_inside_a_word()
    {
        var h = TextFolding.Highlights("Aurora Rise", new[] { "r" });
        Assert.Equal((7, 1), h[0]); // "Rise", not the r inside "Aurora"
    }

    [Fact]
    public void Nothing_to_mark_gives_no_ranges()
    {
        Assert.Empty(TextFolding.Highlights("Wish", new[] { "zzz" }));
        Assert.Empty(TextFolding.Highlights("", new[] { "a" }));
        Assert.Empty(TextFolding.Highlights("Wish", Array.Empty<string>()));
    }

    [Fact]
    public void Accents_do_not_shift_the_ranges()
    {
        Assert.Equal(new[] { (6, 4) }, TextFolding.Highlights("Édith Piaf", new[] { "piaf" }));
        Assert.Equal(new[] { (0, 3) }, TextFolding.Highlights("Édith Piaf", new[] { "edi" }));
    }
}
