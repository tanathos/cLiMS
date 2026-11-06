using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>One answer of a prompt: the key that picks it, the label, and what it does.</summary>
/// <param name="Key">Letter that selects it (case-insensitive).</param>
/// <param name="Label">Text on the button.</param>
/// <param name="Run">Action when chosen.</param>
public sealed record PromptChoice(char Key, string Label, Action Run);

/// <summary>
/// One-row tab strip. The free space on the right shows the pending question (for example "Sync now?") with clickable
/// answers, or a short notice. Hand-drawn so it can show such extras without another layout row.
/// </summary>
public sealed class TabStrip : PaintView
{
    private readonly string[] _titles;
    private readonly List<(int X0, int X1)> _spans = new();
    private readonly List<(int X0, int X1, PromptChoice Choice)> _choiceSpans = new();
    private string? _message;
    private PromptChoice[] _choices = Array.Empty<PromptChoice>();
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

    /// <summary>The answers of the question currently shown, empty when only a notice (or nothing) is shown.</summary>
    public IReadOnlyList<PromptChoice> Choices => _choices;

    public event Action<int>? SelectedChanged;

    /// <summary>Shows a question with answers; replaces any notice. Answered by key or click.</summary>
    public void ShowPrompt(string text, params PromptChoice[] choices)
    {
        _message = text;
        _choices = choices;
        SetNeedsDraw();
    }

    /// <summary>Shows a short notice (no answers). Does not replace a pending question.</summary>
    public void ShowNotice(string text)
    {
        if (_choices.Length > 0)
        {
            return;
        }

        _message = text;
        SetNeedsDraw();
    }

    /// <summary>Clears the question or notice.</summary>
    public void ClearMessage()
    {
        _message = null;
        _choices = Array.Empty<PromptChoice>();
        SetNeedsDraw();
    }

    /// <summary>Runs the answer bound to <paramref name="key"/>. Returns false when no question is open or no answer matches.</summary>
    public bool Answer(char key)
    {
        var choice = _choices.FirstOrDefault(c => char.ToUpperInvariant(c.Key) == char.ToUpperInvariant(key));
        if (choice is null)
        {
            return false;
        }

        ClearMessage();
        choice.Run();
        return true;
    }

    protected override void Paint()
    {
        _spans.Clear();
        _choiceSpans.Clear();
        FillRow(0, 0, W, Theme.Alt);
        var x = 1;
        for (var i = 0; i < _titles.Length; i++)
        {
            var label = $" {i + 1} {_titles[i]} ";
            var attr = i == _selected ? new Attribute(Theme.Bg, Theme.Accent) : new Attribute(Theme.Fg, Theme.BgAlt);
            _spans.Add((x, x + label.Length));
            x = Put(x, 0, label, attr) + 1;
        }

        PaintMessage(x);
    }

    private void PaintMessage(int firstFree)
    {
        if (_message is null)
        {
            return;
        }

        var buttons = _choices.Select(c => $"[ {char.ToUpperInvariant(c.Key)} {c.Label} ]").ToArray();
        var buttonsWidth = buttons.Sum(b => b.Length + 1);
        var textWidth = Math.Max(0, W - firstFree - 3 - buttonsWidth);
        var text = Clip(_message, textWidth);
        var start = Math.Max(firstFree + 2, W - 1 - buttonsWidth - text.Length - 1);

        var x = Put(start, 0, text + " ", new Attribute(_choices.Length > 0 ? Theme.Warn : Theme.FgDim, Theme.BgAlt));
        for (var i = 0; i < buttons.Length; i++)
        {
            var end = Put(x, 0, buttons[i], new Attribute(Theme.Bg, Theme.Accent));
            _choiceSpans.Add((x, end, _choices[i]));
            x = end + 1;
        }
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && mouse.Position is { } p)
        {
            foreach (var (x0, x1, choice) in _choiceSpans)
            {
                if (p.X >= x0 && p.X < x1)
                {
                    Answer(choice.Key);
                    return true;
                }
            }

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
