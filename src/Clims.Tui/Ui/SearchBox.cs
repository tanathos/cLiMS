using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Clims.Tui.Ui;

/// <summary>A text field that shows a dim hint while it is empty and unfocused.</summary>
public sealed class SearchBox : TextField
{
    public string Placeholder { get; init; } = "";

    protected override void OnDrawComplete(DrawContext? context)
    {
        base.OnDrawComplete(context);
        if (Text.Length == 0 && !HasFocus && Placeholder.Length > 0)
        {
            SetAttribute(new Attribute(Theme.FgDim, Theme.BgAlt));
            Move(0, 0);
            AddStr(Placeholder.Length > Viewport.Width ? Placeholder[..Viewport.Width] : Placeholder);
        }
    }
}
