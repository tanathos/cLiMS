using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>
/// Virtualised single-column list: only visible rows are drawn, so 1 000+ rows cost the same as 30.
/// Optional type-ahead and A-Z jump on the first letter. The last column is a scrollbar.
/// </summary>
public sealed class RowList : PaintView
{
    private IReadOnlyList<Row> _rows = Array.Empty<Row>();
    private int _selected = -1;
    private int _offset;
    private bool _revealSelection;
    private string _typed = "";
    private DateTime _typedAt;

    public RowList()
    {
        CanFocus = true;
    }

    public bool TypeAhead { get; init; }

    public bool Zebra { get; init; }

    /// <summary>Optional small clickable glyph drawn after the text of some rows (null = none).</summary>
    public Func<Row, string?>? IconFor { get; init; }

    /// <summary>The glyph of a row was clicked (the row is not activated).</summary>
    public event Action<Row>? IconClicked;

    private static int IconX(Row row) => 1 + row.Text.Length + 2;

    public IReadOnlyList<Row> Rows
    {
        get => _rows;
        set
        {
            _rows = value;
            _offset = 0;
            _selected = -1;
            Select(NextSelectable(-1, 1), raise: false);
            SetNeedsDraw();
        }
    }

    public int SelectedIndex => _selected;

    public Row? SelectedRow => _selected >= 0 && _selected < _rows.Count ? _rows[_selected] : null;

    public event Action<Row>? SelectionChanged;

    public event Action<Row>? RowActivated;

    public void SelectIndex(int index) => Select(index, raise: true);

    protected override void Paint()
    {
        // Applied at paint time, not in Select: the view has no size yet when the first selection is made.
        // Only when the selection moved, otherwise wheel scrolling would be snapped back to the selected row.
        if (_revealSelection && _selected >= 0 && H > 0)
        {
            _offset = _selected < _offset ? _selected : _selected >= _offset + H ? _selected - H + 1 : _offset;
            _revealSelection = false;
        }

        _offset = Math.Clamp(_offset, 0, Math.Max(0, _rows.Count - H));

        var focused = HasFocus;
        var contentWidth = W - 1;
        for (var y = 0; y < H; y++)
        {
            var i = _offset + y;
            if (i >= _rows.Count)
            {
                break;
            }

            var row = _rows[i];
            var attr = i == _selected
                ? (focused ? Theme.Selected : Theme.SelectedDim)
                : row.Kind == RowKind.Header ? Theme.Heading
                : row.Kind == RowKind.More ? Theme.Dim
                : Zebra && i % 2 == 1 ? Theme.Alt : Theme.Normal;

            FillRow(y, 0, contentWidth, attr);
            var right = row.Right.Length > 0 ? row.Right + " " : "";
            Put(1, y, Clip(row.Text, contentWidth - 1 - right.Length), attr);
            if (IconFor?.Invoke(row) is { } icon && IconX(row) + icon.Length < contentWidth - right.Length)
            {
                Put(IconX(row), y, icon, i == _selected ? attr : new Attribute(Theme.Accent, attr.Background));
            }

            if (right.Length > 0)
            {
                Put(contentWidth - right.Length, y, right, i == _selected ? attr : new Attribute(Theme.FgDim, attr.Background));
            }
        }

        PaintScrollbar(_rows.Count, _offset, H);
    }

    protected override bool OnKeyDown(Key key)
    {
        switch (key.KeyCode)
        {
            case KeyCode.CursorDown:
                Select(NextSelectable(_selected, 1), true);
                return true;
            case KeyCode.CursorUp:
                Select(NextSelectable(_selected, -1), true);
                return true;
            case KeyCode.PageDown:
                Select(NextSelectable(Math.Min(_rows.Count - 1, _selected + Math.Max(1, H - 1)) - 1, 1), true);
                return true;
            case KeyCode.PageUp:
                Select(NextSelectable(Math.Max(0, _selected - Math.Max(1, H - 1)) + 1, -1), true);
                return true;
            case KeyCode.Home:
                Select(NextSelectable(-1, 1), true);
                return true;
            case KeyCode.End:
                Select(NextSelectable(_rows.Count, -1), true);
                return true;
            case KeyCode.Enter:
                if (SelectedRow is { } r)
                {
                    RowActivated?.Invoke(r);
                }

                return true;
        }

        if (TypeAhead && !key.IsCtrl && !key.IsAlt && key.AsRune.Value is > 32 and < 0xFFFF
            && char.IsLetterOrDigit((char)key.AsRune.Value))
        {
            TypeChar(char.ToLowerInvariant((char)key.AsRune.Value));
            return true;
        }

        return base.OnKeyDown(key);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        // Wheel works on the hovered list whether or not it has focus.
        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            Scroll(3);
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            Scroll(-3);
            return true;
        }

        var click = mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) || mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked);
        var doubleClick = mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked);
        var press = mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed);
        if ((click || press) && mouse.Position is { } p)
        {
            if (p.X >= W - 1 && _rows.Count > H)
            {
                _offset = OffsetFromScrollbar(p.Y, _rows.Count, H);
                SetNeedsDraw();
                return true;
            }

            if (click)
            {
                SetFocus();
                var i = _offset + p.Y;
                if (i >= 0 && i < _rows.Count && IconFor?.Invoke(_rows[i]) is { } glyph
                    && p.X >= IconX(_rows[i]) - 1 && p.X <= IconX(_rows[i]) + glyph.Length)
                {
                    SetFocus();
                    IconClicked?.Invoke(_rows[i]);
                    return true;
                }

                if (i >= 0 && i < _rows.Count && _rows[i].Kind != RowKind.Header)
                {
                    Select(i, true);
                    if (doubleClick)
                    {
                        RowActivated?.Invoke(_rows[i]);
                    }
                }

                return true;
            }
        }

        return base.OnMouseEvent(mouse);
    }

    private void TypeChar(char c)
    {
        var now = DateTime.UtcNow;
        _typed = (now - _typedAt).TotalMilliseconds > 900 ? c.ToString() : _typed + c;
        _typedAt = now;

        // Skip a leading article the same way the fake library sorts, so "the" does not hide everything.
        for (var i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Kind != RowKind.Item)
            {
                continue;
            }

            var name = _rows[i].Text;
            if (name.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
            {
                name = name[4..];
            }

            if (name.StartsWith(_typed, StringComparison.OrdinalIgnoreCase))
            {
                Select(i, true);
                return;
            }
        }
    }

    private int NextSelectable(int from, int step)
    {
        for (var i = from + step; i >= 0 && i < _rows.Count; i += step)
        {
            if (_rows[i].Kind != RowKind.Header)
            {
                return i;
            }
        }

        return from >= 0 && from < _rows.Count ? from : -1;
    }

    private void Scroll(int delta)
    {
        _offset = Math.Clamp(_offset + delta, 0, Math.Max(0, _rows.Count - H));
        SetNeedsDraw();
    }

    private void Select(int index, bool raise)
    {
        if (index < 0 || index >= _rows.Count)
        {
            return;
        }

        _selected = index;
        _revealSelection = true;
        SetNeedsDraw();
        if (raise)
        {
            SelectionChanged?.Invoke(_rows[index]);
        }
    }
}
