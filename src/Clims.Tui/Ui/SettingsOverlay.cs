using Clims.Tui.Settings;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>
/// The settings page, drawn over the tabs (like the help): one tab per group of settings, a row per setting. Every change is
/// saved as soon as it is made. The keys are handled here but fed by the main window, which sees them first.
/// </summary>
public sealed class SettingsOverlay : PaintView
{
    private const int TabsY = 3;
    private const int RowsY = 5;
    private const int LabelX = 3;
    private const int ValueX = 34;
    private const int HintX = 60;

    private readonly SettingsModel _model;
    private readonly List<(int X0, int X1)> _tabSpans = new();
    private int _offset;

    public SettingsOverlay(SettingsModel model)
    {
        _model = model;
        // Focusable, so that hiding the pages under it does not hand the keyboard to the search box.
        CanFocus = true;
    }

    /// <summary>Esc was pressed (or the key that opens the page): the page is to be closed.</summary>
    public event Action? CloseRequested;

    /// <summary>The keys that open and close the page: F10 and Alt+S.</summary>
    public static bool IsToggleKey(Key key) => key == Key.F10 || key == Key.S.WithAlt;

    public SettingsModel Model => _model;

    private int Room => Math.Max(1, H - RowsY - 3);

    /// <summary>Back to the first tab, for when the page is opened again.</summary>
    public void Reset()
    {
        _model.SetPage(0);
        _offset = 0;
        SetNeedsDraw();
    }

    protected override void Paint()
    {
        var head = new Attribute(Theme.Accent, Theme.Bg);
        var dim = new Attribute(Theme.FgDim, Theme.Bg);
        var text = Theme.Normal;
        Put(2, 1, "cLiMS settings", head);
        Put(18, 1, Clip("Esc close · Tab next tab · ↑↓ select · ←→ change · Enter change or edit · changes are saved at once", W - 20), dim);

        _tabSpans.Clear();
        var x = 2;
        for (var i = 0; i < _model.Pages.Count; i++)
        {
            var label = $" {_model.Pages[i].Title} ";
            var attr = i == _model.PageIndex ? Theme.Selected : new Attribute(Theme.Fg, Theme.BgAlt);
            _tabSpans.Add((x, x + label.Length));
            x = Put(x, TabsY, label, attr) + 1;
        }

        var items = _model.Items;
        _offset = Math.Clamp(_offset, 0, Math.Max(0, items.Count - Room));
        if (_model.Selected < _offset)
        {
            _offset = _model.Selected;
        }
        else if (_model.Selected >= _offset + Room)
        {
            _offset = _model.Selected - Room + 1;
        }

        for (var row = 0; row < Room && _offset + row < items.Count; row++)
        {
            var index = _offset + row;
            var item = items[index];
            var y = RowsY + row;
            var selected = index == _model.Selected;
            var rowAttr = selected ? Theme.Selected : text;
            FillRow(y, 1, W - 1, rowAttr);

            var label = (item.Kind == SettingKind.Server ? (item.Connected ? "● " : "  ") : "") + item.Label;
            Put(LabelX, y, Clip(label, ValueX - LabelX - 2), item.Kind == SettingKind.Info && !selected ? dim : rowAttr);
            if (item.NeedsRestart)
            {
                Put(ValueX - 3, y, "↻", selected ? rowAttr : new Attribute(Theme.Warn, Theme.Bg));
            }

            var valueX = Put(ValueX, y, ValueText(item, selected), selected ? rowAttr : ValueAttr(item));
            if (item.Hint.Length > 0 && W > HintX + 8)
            {
                Put(Math.Max(HintX, valueX + 2), y, Clip(item.Hint, W - Math.Max(HintX, valueX + 2) - 2), selected ? rowAttr : dim);
            }
        }

        if (items.Count > Room)
        {
            Put(W - 2, RowsY, _offset > 0 ? "▲" : " ", dim);
            Put(W - 2, RowsY + Room - 1, _offset + Room < items.Count ? "▼" : " ", dim);
        }

        if (_model.Message is { Length: > 0 } message)
        {
            Put(2, H - 2, Clip(message, W - 4), new Attribute(Theme.Warn, Theme.Bg));
        }
        else
        {
            Put(2, H - 2, Clip(KeyHint(), W - 4), dim);
        }

        if (_model.RestartNeeded)
        {
            Put(2, H - 1, Clip("↻ changes marked ↻ take effect at the next start", W - 4), new Attribute(Theme.Warn, Theme.Bg));
        }
    }

    private string ValueText(SettingItem item, bool selected)
    {
        if (selected && _model.Editing && item.Edit is not null)
        {
            return _model.Buffer + "▏";
        }

        var value = item.Value(_model.Snapshot);
        return item.Kind switch
        {
            SettingKind.Toggle => (value == "on" ? "◉ " : "○ ") + value,
            SettingKind.Choice or SettingKind.Number => "◂ " + value + " ▸",
            _ => value,
        };
    }

    private static Attribute ValueAttr(SettingItem item) => item.Kind switch
    {
        SettingKind.Info or SettingKind.Server => new Attribute(Theme.FgDim, Theme.Bg),
        SettingKind.Action => new Attribute(Theme.Accent, Theme.Bg),
        _ => new Attribute(Theme.Warn, Theme.Bg),
    };

    private string KeyHint() => _model.Current?.Kind switch
    {
        SettingKind.Server => "Enter connect now · u opens at start · e rename · d remove",
        SettingKind.Text => "Enter edit",
        SettingKind.Action => "Enter add a server",
        SettingKind.Info or null => "",
        _ => "←→ or Enter change",
    };

    /// <summary>Handles a key. False when it is not one of the page's (the main window then ignores it as well while the page is open).</summary>
    public bool HandleKey(Key key)
    {
        if (_model.Editing)
        {
            HandleEditKey(key);
            return true;
        }

        if (_model.ConfirmingRemove)
        {
            if (key.AsRune.Value is 'y' or 'Y')
            {
                _model.ConfirmRemove();
            }
            else
            {
                _model.CancelRemove();
            }

            SetNeedsDraw();
            return true;
        }

        var page = Math.Max(1, Room - 1);
        var c = key.AsRune.Value;
        if (key == Key.Esc || IsToggleKey(key))
        {
            CloseRequested?.Invoke();
            return true;
        }

        if (key == Key.Tab)
        {
            _model.NextPage(1);
        }
        else if (key == Key.Tab.WithShift)
        {
            _model.NextPage(-1);
        }
        else if (key == Key.CursorDown)
        {
            _model.Move(1);
        }
        else if (key == Key.CursorUp)
        {
            _model.Move(-1);
        }
        else if (key == Key.PageDown)
        {
            for (var i = 0; i < page; i++)
            {
                _model.Move(1);
            }
        }
        else if (key == Key.PageUp)
        {
            for (var i = 0; i < page; i++)
            {
                _model.Move(-1);
            }
        }
        else if (key == Key.Home)
        {
            _model.MoveTo(0);
        }
        else if (key == Key.End)
        {
            _model.MoveTo(_model.Items.Count - 1);
        }
        else if (key == Key.CursorLeft)
        {
            _model.Change(-1);
        }
        else if (key == Key.CursorRight)
        {
            _model.Change(1);
        }
        else if (key == Key.Enter || key == Key.Space)
        {
            _model.Activate();
        }
        else if (c is 'u' or 'U')
        {
            _model.UseAtStart();
        }
        else if (c is 'e' or 'E')
        {
            _model.BeginEdit();
        }
        else if (c is 'd' or 'D' || key == Key.Delete)
        {
            _model.BeginRemove();
        }

        SetNeedsDraw();
        return true;
    }

    private void HandleEditKey(Key key)
    {
        if (key == Key.Esc)
        {
            _model.CancelEdit();
        }
        else if (key == Key.Enter)
        {
            _model.CommitEdit();
        }
        else if (key == Key.Backspace)
        {
            _model.Backspace();
        }
        else if (key.AsRune.Value is >= 32 and not 127 and < 0xFFFF)
        {
            _model.Type(((char)key.AsRune.Value).ToString());
        }

        SetNeedsDraw();
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            _model.Move(1);
            SetNeedsDraw();
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            _model.Move(-1);
            SetNeedsDraw();
            return true;
        }

        var clicked = mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked);
        var double_ = mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked);
        if (!(clicked || double_) || mouse.Position is not { } p || _model.Editing)
        {
            return base.OnMouseEvent(mouse);
        }

        if (p.Y == TabsY)
        {
            for (var i = 0; i < _tabSpans.Count; i++)
            {
                if (p.X >= _tabSpans[i].X0 && p.X < _tabSpans[i].X1)
                {
                    _model.SetPage(i);
                    _offset = 0;
                    SetNeedsDraw();
                    return true;
                }
            }

            return true;
        }

        var index = _offset + p.Y - RowsY;
        if (p.Y >= RowsY && p.Y < RowsY + Room && index < _model.Items.Count)
        {
            ClickRow(index, p.X, double_);
            SetNeedsDraw();
            return true;
        }

        return base.OnMouseEvent(mouse);
    }

    private void ClickRow(int index, int x, bool doubleClick)
    {
        _model.MoveTo(index);
        switch (_model.Current)
        {
            case { Kind: SettingKind.Toggle } when x >= ValueX:
                _model.Change(1);
                break;
            case { Kind: SettingKind.Choice or SettingKind.Number } when x >= ValueX:
                _model.Change(x < ValueX + 2 ? -1 : 1);
                break;
            case { Kind: SettingKind.Action }:
                _model.Activate();
                break;
            case { Kind: SettingKind.Server or SettingKind.Text } when doubleClick:
                _model.Activate();
                break;
        }
    }
}
