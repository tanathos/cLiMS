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

    protected override void Paint() => PutLegend(1, 0, Math.Max(0, W - 1), _items, Theme.Bg);
}
