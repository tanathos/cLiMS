using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>
/// Invisible view over the whole window, under the popup: it takes the clicks that miss the menu (which then only close it)
/// so they do not reach the lists underneath.
/// </summary>
public sealed class ClickShield : Terminal.Gui.ViewBase.View
{
    public ClickShield()
    {
        X = 0;
        Y = 0;
        Width = Terminal.Gui.ViewBase.Dim.Fill();
        Height = Terminal.Gui.ViewBase.Dim.Fill();
        // Transparent: the views under it are still drawn (and keep their mouse events off: this view takes them).
        ViewportSettings = Terminal.Gui.ViewBase.ViewportSettingsFlags.Transparent;
        CanFocus = false;
    }

    /// <summary>Any button, or the wheel, was used outside the menu.</summary>
    public event Action? Clicked;


    protected override bool OnMouseEvent(Terminal.Gui.Input.Mouse mouse)
    {
        var f = mouse.Flags;
        if (f.HasFlag(Terminal.Gui.Input.MouseFlags.LeftButtonClicked) || f.HasFlag(Terminal.Gui.Input.MouseFlags.RightButtonClicked)
            || f.HasFlag(Terminal.Gui.Input.MouseFlags.MiddleButtonClicked) || f.HasFlag(Terminal.Gui.Input.MouseFlags.WheeledDown)
            || f.HasFlag(Terminal.Gui.Input.MouseFlags.WheeledUp))
        {
            Clicked?.Invoke();
        }

        return true;
    }
}
