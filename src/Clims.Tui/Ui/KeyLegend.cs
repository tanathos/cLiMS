namespace Clims.Tui.Ui;

/// <summary>The keys of a tab on its last row: "key label" pairs separated by a centred dot.</summary>
public sealed class KeyLegend : PaintView
{
    private IReadOnlyList<(string Key, string Label)> _items = [];

    public IReadOnlyList<(string Key, string Label)> Items
    {
        get => _items;
        set
        {
            _items = value;
            SetNeedsDraw();
        }
    }

    protected override void Paint()
    {
        var x = 1;
        var key = new Attribute(Theme.Accent, Theme.Bg);
        var label = new Attribute(Theme.FgDim, Theme.Bg);
        for (var i = 0; i < _items.Count; i++)
        {
            if (i > 0)
            {
                x = Put(x, 0, " · ", label);
            }

            x = Put(x, 0, _items[i].Key + " ", key);
            x = Put(x, 0, _items[i].Label, label);
        }
    }
}
