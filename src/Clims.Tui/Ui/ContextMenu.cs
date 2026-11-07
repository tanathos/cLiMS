using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>
/// Small popup list opened by a right click. It only draws and keeps the highlighted entry; <see cref="MainWindow"/>
/// feeds it keys and mouse events (so a click anywhere else can close it) and runs the chosen entry.
/// </summary>
public sealed class ContextMenu : PaintView
{
    private IReadOnlyList<MenuEntry> _entries = Array.Empty<MenuEntry>();
    private int _selected;

    protected override Attribute Base => new(Theme.Fg, Theme.BgAlt);

    protected override bool Dimmable => false;

    public int Selected => _selected;

    public IReadOnlyList<MenuEntry> Entries => _entries;

    /// <summary>Size of the popup for the entries: a one-cell border and one cell of padding on each side.</summary>
    public static (int Width, int Height) SizeFor(IReadOnlyList<MenuEntry> entries) =>
        (entries.Count == 0 ? 4 : entries.Max(e => e.Label.Length) + 4, entries.Count + 2);

    /// <summary>Top-left cell for a popup of the given size opened at (x, y): moved back inside the screen when it would stick out.</summary>
    public static (int X, int Y) Place(int x, int y, int width, int height, int screenWidth, int screenHeight) =>
        (Math.Max(0, Math.Min(x, screenWidth - width)), Math.Max(0, Math.Min(y, screenHeight - height)));

    public void Open(IReadOnlyList<MenuEntry> entries, int x, int y, int screenWidth, int screenHeight)
    {
        _entries = entries;
        _selected = 0;
        var (w, h) = SizeFor(entries);
        var (px, py) = Place(x, y, w, h, screenWidth, screenHeight);
        X = px;
        Y = py;
        Width = w;
        Height = h;
        Visible = true;
        SetNeedsDraw();
    }

    public void Close()
    {
        Visible = false;
        _entries = Array.Empty<MenuEntry>();
    }

    public void MoveSelection(int delta)
    {
        if (_entries.Count == 0)
        {
            return;
        }

        _selected = (_selected + delta + _entries.Count) % _entries.Count;
        SetNeedsDraw();
    }

    /// <summary>The entry under a screen cell, or -1 when the cell is outside the entries (the border counts as outside).</summary>
    public int EntryAt(int screenX, int screenY)
    {
        var frame = FrameToScreen();
        var row = screenY - frame.Y - 1;
        var inside = screenX > frame.X && screenX < frame.X + frame.Width - 1;
        return inside && row >= 0 && row < _entries.Count ? row : -1;
    }

    /// <summary>True when the cell is on the popup, border included.</summary>
    public bool Contains(int screenX, int screenY) => Visible && FrameToScreen().Contains(screenX, screenY);

    public void Select(int index)
    {
        if (index >= 0 && index < _entries.Count && index != _selected)
        {
            _selected = index;
            SetNeedsDraw();
        }
    }

    public void RunSelected()
    {
        if (_selected >= 0 && _selected < _entries.Count)
        {
            _entries[_selected].Run();
        }
    }

    /// <summary>An entry was clicked (it is the selected one).</summary>
    public event Action? Chosen;

    protected override bool OnMouseEvent(Terminal.Gui.Input.Mouse mouse)
    {
        if (mouse.Position is { } p)
        {
            var row = p.Y - 1;
            if (p.X > 0 && p.X < W - 1 && row >= 0 && row < _entries.Count)
            {
                Select(row);
                if (mouse.Flags.HasFlag(Terminal.Gui.Input.MouseFlags.LeftButtonClicked))
                {
                    Chosen?.Invoke();
                }
            }
        }

        return true;
    }

    protected override void Paint()
    {
        var frame = new Attribute(Theme.FgDim, Theme.BgAlt);
        var text = new Attribute(Theme.Fg, Theme.BgAlt);
        var chosen = new Attribute(Theme.Fg, Theme.BgSelected, TextStyle.Bold);
        if (W < 4 || H < 3)
        {
            return;
        }

        Put(0, 0, "┌" + new string('─', W - 2) + "┐", frame);
        Put(0, H - 1, "└" + new string('─', W - 2) + "┘", frame);
        for (var i = 0; i < _entries.Count; i++)
        {
            var on = i == _selected;
            Put(0, i + 1, "│", frame);
            Put(W - 1, i + 1, "│", frame);
            Put(1, i + 1, (" " + _entries[i].Label).PadRight(W - 2), on ? chosen : text);
        }
    }
}
