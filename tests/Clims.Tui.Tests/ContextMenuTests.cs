using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public class ContextMenuTests
{
    private static readonly IReadOnlyList<MenuEntry> Two = [new("Play", () => { }), new("Enqueue", () => { })];

    [Fact]
    public void Size_fits_the_longest_label_plus_border_and_padding()
    {
        Assert.Equal((11, 4), ContextMenu.SizeFor(Two));
    }

    [Theory]
    [InlineData(10, 5, 10, 5)]
    [InlineData(95, 5, 89, 5)]
    [InlineData(10, 28, 10, 26)]
    [InlineData(-3, -1, 0, 0)]
    public void Place_keeps_the_popup_on_screen(int x, int y, int expectedX, int expectedY)
    {
        Assert.Equal((expectedX, expectedY), ContextMenu.Place(x, y, 11, 4, 100, 30));
    }

    [Fact]
    public void Selection_wraps_around()
    {
        var menu = new ContextMenu();
        menu.Open(Two, 0, 0, 100, 30);
        menu.MoveSelection(-1);
        Assert.Equal(1, menu.Selected);
        menu.MoveSelection(1);
        Assert.Equal(0, menu.Selected);
    }

    [Fact]
    public void RunSelected_runs_the_highlighted_entry()
    {
        var ran = "";
        var menu = new ContextMenu();
        menu.Open([new("Play", () => ran += "p"), new("Enqueue", () => ran += "e")], 0, 0, 100, 30);
        menu.MoveSelection(1);
        menu.RunSelected();
        Assert.Equal("e", ran);
    }
}
