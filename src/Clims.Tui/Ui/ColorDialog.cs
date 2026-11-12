using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Clims.Tui.Ui;

/// <summary>A modal dialog with Terminal.Gui's colour picker, for fine tuning one colour of the theme.</summary>
public static class ColorDialog
{
    /// <summary>Shows the dialog and returns the colour chosen with OK, or null when it was cancelled (Esc or Cancel).</summary>
    /// <param name="app">The running application.</param>
    /// <param name="name">What the colour is for (the dialog's title).</param>
    /// <param name="initial">The colour to start from.</param>
    public static Color? Show(IApplication app, string name, Color initial)
    {
        using var dialog = new Dialog { Title = $"Colour: {name}", BorderStyle = LineStyle.Single, ShadowStyle = ShadowStyles.None };
        dialog.SetScheme(Theme.Dark);
        var picker = new ColorPicker { X = 0, Y = 0 };
        picker.Style.ColorModel = ColorModel.HSL;
        picker.Style.ShowTextFields = true;
        picker.Style.ShowColorName = false;
        picker.ApplyStyleChanges();
        picker.Value = initial;
        dialog.Add(picker);
        dialog.AddButton(Flat("_Cancel"));
        dialog.AddButton(Flat("_OK"));

        app.Run(dialog);

        // Button 1 is OK; Esc leaves no result.
        return dialog.Result == 1 ? picker.SelectedColor : null;
    }

    /// <summary>A button like the app's own: a plain filled label, no brackets, no shadow.</summary>
    private static Button Flat(string title)
    {
        var button = new Button { Title = title, NoDecorations = true, ShadowStyle = ShadowStyles.None };
        button.SetScheme(new Scheme(new Attribute(Theme.Fg, Theme.BgAlt))
        {
            Focus = Theme.Selected,
            HotNormal = new Attribute(Theme.Accent, Theme.BgAlt),
            HotFocus = new Attribute(Theme.Accent, Theme.BgSelected),
        });
        return button;
    }
}
