using Clims.Catalog;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>
/// Virtualised list of search results with variable row heights: only the rows in view are drawn. Matches of the query are
/// highlighted, thumbnails are requested lazily through <see cref="CoverArt.Source"/> (and cancelled when they scroll
/// away), keys <c>a</c> / <c>n</c> ask to add or play next. The last column is the scrollbar.
/// </summary>
public sealed class ResultsView : PaintView
{
    private const int ThumbCols = 4;
    private const int ThumbRows = 2;
    private const int TextX = ThumbCols + 2;

    private IReadOnlyList<ResultItem> _items = Array.Empty<ResultItem>();
    private IReadOnlyList<string> _words = Array.Empty<string>();
    private int _selected = -1;
    private int _offset;
    private bool _reveal;

    public ResultsView()
    {
        CanFocus = true;
    }

    /// <summary>Enter or a click on a row.</summary>
    public event Action<ResultItem>? ItemActivated;

    /// <summary>Key <c>a</c> (add to queue) or <c>n</c> (play next) on the selected row.</summary>
    public event Action<QueueAction, ResultItem>? QueueRequested;

    /// <summary>Right click on an album or track row (now selected), with the screen cell of the click.</summary>
    public event Action<ResultItem, System.Drawing.Point>? ContextRequested;

    public IReadOnlyList<ResultItem> Items => _items;

    public ResultItem? SelectedItem => _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

    public int SelectedIndex => _selected;

    /// <summary>Folded query words, used to highlight matches.</summary>
    public IReadOnlyList<string> Words
    {
        get => _words;
        set
        {
            _words = value;
            SetNeedsDraw();
        }
    }

    /// <summary>Replaces the rows. With <paramref name="keepPosition"/> the selection index and scroll are kept (a refresh of the same query).</summary>
    public void SetItems(IReadOnlyList<ResultItem> items, bool keepPosition)
    {
        var oldSelected = _selected;
        var oldOffset = _offset;
        _items = items;
        _offset = keepPosition ? Math.Min(oldOffset, Math.Max(0, items.Count - 1)) : 0;
        _selected = -1;
        var wanted = keepPosition ? Math.Min(oldSelected, items.Count - 1) : -1;
        Select(wanted >= 0 && items[wanted].Selectable ? wanted : NextSelectable(Math.Max(-1, wanted - 1), 1), reveal: !keepPosition);
        SetNeedsDraw();
    }

    /// <summary>Selects a row (for example after expanding a group).</summary>
    public void SelectIndex(int index) => Select(index, reveal: true);

    protected override void Paint()
    {
        Reveal();
        _offset = Math.Clamp(_offset, 0, MaxOffset());

        var contentWidth = W - 1;
        var y = 0;
        var shown = 0;
        for (var i = _offset; i < _items.Count && y < H; i++)
        {
            var item = _items[i];
            PaintItem(item, i == _selected, y, contentWidth);
            y += item.Height;
            if (y <= H)
            {
                shown++;
            }
        }

        PaintScrollbar(_items.Count, _offset, Math.Max(1, shown));
    }

    private void PaintItem(ResultItem item, bool selected, int y, int width)
    {
        var focused = HasFocus;
        var back = selected ? (focused ? Theme.Selected : Theme.SelectedDim) : Theme.Normal;
        var mark = new Attribute(Theme.Warn, back.Background);
        var dim = new Attribute(Theme.FgDim, back.Background);

        for (var r = 0; r < item.Height; r++)
        {
            FillRow(y + r, 0, width, back);
        }

        switch (item.Kind)
        {
            case ResultKind.Header:
                Put(1, y, Clip(item.Text, width - 2), Theme.Heading);
                return;
            case ResultKind.Info:
                Put(1, y, Clip(item.Text, width - 2), Theme.Dim);
                return;
            case ResultKind.More:
                Put(TextX, y, Clip(item.Text, width - TextX - 1), selected ? back : Theme.Dim);
                return;
            case ResultKind.Genre:
                Put(2, y, "♯", new Attribute(Theme.Accent, back.Background));
                break;
            case ResultKind.Artist:
                PaintIcon(y);
                break;
            default:
                if (item.Cover is { } key)
                {
                    DrawCover(1, y, ThumbCols, ThumbRows, key);
                }

                break;
        }

        var right = item.Right.Length > 0 ? item.Right + " " : "";
        var textWidth = Math.Max(0, width - TextX - 1 - right.Length);
        Marked(TextX, y, item.Text, textWidth, back, mark);
        if (right.Length > 0)
        {
            Put(width - right.Length, y, right, dim);
        }

        if (item.Height > 1 && item.Detail.Length > 0)
        {
            Marked(TextX, y + 1, item.Detail, Math.Max(0, width - TextX - 1), dim, mark);
        }
    }

    /// <summary>LMS has no artist portraits: a small dim block with a note.</summary>
    private void PaintIcon(int y)
    {
        var attr = new Attribute(Theme.Accent, Theme.BgAlt);
        for (var r = 0; r < ThumbRows; r++)
        {
            FillRow(y + r, 1, 1 + ThumbCols, attr);
        }

        Put(1 + ThumbCols / 2 - 1, y, "♫", attr);
        Put(1 + ThumbCols / 2, y + 1, "♪", attr);
    }

    /// <summary>Draws text in <paramref name="normal"/> with the query matches in <paramref name="match"/>.</summary>
    private void Marked(int x, int y, string text, int width, Attribute normal, Attribute match)
    {
        var clipped = Clip(text, width);
        if (clipped.Length == 0)
        {
            return;
        }

        Put(x, y, clipped, normal);
        var limit = clipped.Length == text.Length ? clipped.Length : clipped.Length - 1; // the last char is the ellipsis
        foreach (var (start, length) in TextFolding.Highlights(text, _words))
        {
            if (start >= limit)
            {
                break;
            }

            Put(x + start, y, clipped.Substring(start, Math.Min(length, limit - start)), match);
        }
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
                Select(NextSelectable(Math.Min(_items.Count - 1, _selected + Math.Max(1, H / 2 - 1)) - 1, 1), true);
                return true;
            case KeyCode.PageUp:
                Select(NextSelectable(Math.Max(0, _selected - Math.Max(1, H / 2 - 1)) + 1, -1), true);
                return true;
            case KeyCode.Home:
                Select(NextSelectable(-1, 1), true);
                return true;
            case KeyCode.End:
                Select(NextSelectable(_items.Count, -1), true);
                return true;
            case KeyCode.Enter:
                ActivateSelected();
                return true;
        }

        if (!key.IsCtrl && !key.IsAlt && SelectedItem is not null)
        {
            switch (key.AsRune.Value)
            {
                case 'a':
                    RequestQueue(QueueAction.Add);
                    return true;
                case 'n':
                    RequestQueue(QueueAction.PlayNext);
                    return true;
            }
        }

        return base.OnKeyDown(key);
    }

    /// <summary>Activates the selected row, as Enter does.</summary>
    public void ActivateSelected()
    {
        if (SelectedItem is { } item)
        {
            ItemActivated?.Invoke(item);
        }
    }

    /// <summary>Asks to add the selected row to the queue or play it next, as the keys <c>a</c> and <c>n</c> do.</summary>
    public void RequestQueue(QueueAction action)
    {
        if (SelectedItem is { } item)
        {
            QueueRequested?.Invoke(action, item);
        }
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (StrayClick(mouse))
        {
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            Scroll(2);
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            Scroll(-2);
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.RightButtonClicked) && mouse.Position is { } rp)
        {
            SetFocus();
            var at = ItemAt(rp.Y);
            if (at >= 0 && _items[at].Kind is ResultKind.Album or ResultKind.Track)
            {
                Select(at, true);
                ContextRequested?.Invoke(_items[at], mouse.ScreenPosition);
            }

            return true;
        }

        if (ScrollbarMouse(mouse, _items.Count > 0 && MaxOffset() > 0, y =>
            {
                _offset = Math.Clamp(OffsetFromScrollbar(y, _items.Count, Math.Max(1, FitCount())), 0, MaxOffset());
                SetNeedsDraw();
            }))
        {
            return true;
        }

        var click = mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) || mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked);
        var doubleClick = mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked);
        if (click && mouse.Position is { } p)
        {
            if (click)
            {
                SetFocus();
                var i = ItemAt(p.Y);
                if (i >= 0 && _items[i].Selectable)
                {
                    Select(i, true);
                    if (doubleClick)
                    {
                        ItemActivated?.Invoke(_items[i]);
                    }
                }

                return true;
            }
        }

        return base.OnMouseEvent(mouse);
    }

    private int ItemAt(int row)
    {
        var y = 0;
        for (var i = _offset; i < _items.Count; i++)
        {
            y += _items[i].Height;
            if (row < y)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>How many rows fit completely starting at the current offset.</summary>
    private int FitCount()
    {
        var y = 0;
        var n = 0;
        for (var i = _offset; i < _items.Count; i++)
        {
            y += _items[i].Height;
            if (y > H)
            {
                break;
            }

            n++;
        }

        return n;
    }

    /// <summary>The first row index from which everything after it still fits in the view.</summary>
    private int MaxOffset()
    {
        var y = 0;
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            y += _items[i].Height;
            if (y > H)
            {
                return i + 1 >= _items.Count ? Math.Max(0, _items.Count - 1) : i + 1;
            }
        }

        return 0;
    }

    /// <summary>Applied at paint time (the view has no size when the first selection is made), only when the selection moved.</summary>
    private void Reveal()
    {
        if (!_reveal || _selected < 0 || H <= 0)
        {
            return;
        }

        _reveal = false;
        if (_selected < _offset)
        {
            _offset = _selected;
            return;
        }

        while (_offset < _selected && HeightBetween(_offset, _selected) > H)
        {
            _offset++;
        }
    }

    /// <summary>Total height of rows <paramref name="from"/> to <paramref name="to"/> inclusive.</summary>
    private int HeightBetween(int from, int to)
    {
        var h = 0;
        for (var i = from; i <= to && i < _items.Count; i++)
        {
            h += _items[i].Height;
        }

        return h;
    }

    private int NextSelectable(int from, int step)
    {
        for (var i = from + step; i >= 0 && i < _items.Count; i += step)
        {
            if (_items[i].Selectable)
            {
                return i;
            }
        }

        return from >= 0 && from < _items.Count && _items[from].Selectable ? from : -1;
    }

    private void Scroll(int delta)
    {
        _offset = Math.Clamp(_offset + delta, 0, MaxOffset());
        SetNeedsDraw();
    }

    private void Select(int index, bool reveal)
    {
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        _selected = index;
        _reveal = reveal;
        SetNeedsDraw();
    }
}
