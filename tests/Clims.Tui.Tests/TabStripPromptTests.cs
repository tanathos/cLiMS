using Clims.Catalog;
using Clims.Tui;
using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public class TabStripPromptTests
{
    // Answering needs no terminal: Answer/Choices are plain state on the view.
    [Fact]
    public void A_prompt_is_answered_by_key_case_insensitively_and_then_cleared()
    {
        var strip = new TabStrip("A", "B");
        var ran = new List<string>();
        strip.ShowPrompt("Sync?", new PromptChoice('Y', "Yes", () => ran.Add("yes")), new PromptChoice('L', "Later", () => ran.Add("later")));

        Assert.Equal(2, strip.Choices.Count);
        Assert.True(strip.Answer('l'));
        Assert.Equal(new[] { "later" }, ran);
        Assert.Empty(strip.Choices);
        Assert.False(strip.Answer('y')); // the question is gone
    }

    [Fact]
    public void An_unknown_key_leaves_the_prompt_open()
    {
        var strip = new TabStrip("A");
        strip.ShowPrompt("Sync?", new PromptChoice('Y', "Yes", () => { }));
        Assert.False(strip.Answer('x'));
        Assert.Single(strip.Choices);
    }

    [Fact]
    public void A_notice_never_replaces_a_pending_question()
    {
        var strip = new TabStrip("A");
        strip.ShowPrompt("Sync?", new PromptChoice('Y', "Yes", () => { }));
        strip.ShowNotice("something else");
        Assert.Single(strip.Choices);
    }
}
