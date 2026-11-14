using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Clims.Tui.Ui;

/// <summary>A caption and a text field on the last row of a tab ("Save queue as:", "Search Spotty:").</summary>
public sealed class PromptBar : View
{
    private readonly Caption _caption = new();

    public PromptBar()
    {
        CanFocus = true;
        Field = new TextField { X = 22, Y = 0, Width = 40, Height = 1 };
        _caption.X = 1;
        _caption.Y = 0;
        _caption.Width = 21;
        _caption.Height = 1;
        Add(_caption, Field);
    }

    public TextField Field { get; }

    /// <summary>What the field is for.</summary>
    public string Prompt
    {
        get => _caption.Label;
        set
        {
            _caption.Label = value;
            _caption.SetNeedsDraw();
        }
    }

    private sealed class Caption : PaintView
    {
        public string Label { get; set; } = "";

        protected override void Paint() => Put(0, 0, Clip(Label, W), Theme.Heading);
    }
}
