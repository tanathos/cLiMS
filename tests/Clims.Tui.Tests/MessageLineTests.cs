using Clims.Catalog;
using Clims.Tui;
using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public class MessageLineTests
{
    // Answering needs no terminal: Answer/Choices are plain state on the view.
    [Fact]
    public void A_prompt_is_answered_by_key_case_insensitively_and_then_cleared()
    {
        var line = new MessageLine();
        var ran = new List<string>();
        line.ShowPrompt("Sync?", new PromptChoice('Y', "Yes", () => ran.Add("yes")), new PromptChoice('L', "Later", () => ran.Add("later")));

        Assert.Equal(2, line.Choices.Count);
        Assert.True(line.Answer('l'));
        Assert.Equal(new[] { "later" }, ran);
        Assert.Empty(line.Choices);
        Assert.False(line.Answer('y')); // the question is gone
    }

    [Fact]
    public void An_unknown_key_leaves_the_prompt_open()
    {
        var line = new MessageLine();
        line.ShowPrompt("Sync?", new PromptChoice('Y', "Yes", () => { }));
        Assert.False(line.Answer('x'));
        Assert.Single(line.Choices);
    }

    [Fact]
    public void A_notice_never_replaces_a_pending_question()
    {
        var line = new MessageLine();
        line.ShowPrompt("Sync?", new PromptChoice('Y', "Yes", () => { }));
        line.ShowNotice("something else");
        Assert.Single(line.Choices);
    }
}
