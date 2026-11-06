using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>One-row tab strip. Hand-drawn so it can later show badges (queue length, sync dot).</summary>
public sealed class TabStrip : PaintView
{
    private readonly string[] _titles;
    private readonly List<(int X0, int X1)> _spans = new();
    private int _selected;

    public TabStrip(params string[] titles)
    {
        _titles = titles;
    }

    public int Selected
    {
        get => _selected;
        set
        {
            if (value == _selected || value < 0 || value >= _titles.Length)
            {
                return;
            }

            _selected = value;
            SetNeedsDraw();
            SelectedChanged?.Invoke(value);
        }
    }

    public event Action<int>? SelectedChanged;

    protected override void Paint()
    {
        _spans.Clear();
        FillRow(0, 0, W, Theme.Alt);
        var x = 1;
        for (var i = 0; i < _titles.Length; i++)
        {
            var label = $" {i + 1} {_titles[i]} ";
            var attr = i == _selected ? new Attribute(Theme.Bg, Theme.Accent) : new Attribute(Theme.Fg, Theme.BgAlt);
            _spans.Add((x, x + label.Length));
            x = Put(x, 0, label, attr) + 1;
        }
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && mouse.Position is { } p)
        {
            for (var i = 0; i < _spans.Count; i++)
            {
                if (p.X >= _spans[i].X0 && p.X < _spans[i].X1)
                {
                    Selected = i;
                    return true;
                }
            }
        }

        return base.OnMouseEvent(mouse);
    }
}
