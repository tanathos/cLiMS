using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>
/// The row under the header's content row, just above the tab strip: the pending question (for example "Sync now?") with
/// clickable answers, or a short notice, right-aligned. It is the blank row of the header bar, so it costs no layout row.
/// </summary>
public sealed class MessageLine : PaintView
{
    private const int Pad = 2; // the header's side padding: the text ends where the "⇄ Server" button ends
    private const int ButtonGap = 1;

    private readonly List<(int X0, int X1, PromptChoice Choice)> _choiceSpans = new();
    private string? _message;
    private PromptChoice[] _choices = Array.Empty<PromptChoice>();

    /// <summary>The answers of the question currently shown, empty when only a notice (or nothing) is shown.</summary>
    public IReadOnlyList<PromptChoice> Choices => _choices;

    protected override Attribute Base => new(Theme.Fg, Theme.BgHeader);

    protected override bool TracksHover => true;

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
        _choiceSpans.Clear();
        if (_message is null)
        {
            return;
        }

        var buttons = _choices.Select(c => $"[ {char.ToUpperInvariant(c.Key)} {c.Label} ]").ToArray();
        var buttonsWidth = buttons.Sum(b => b.Length) + ButtonGap * buttons.Length;
        var text = Clip(_message, Math.Max(0, W - 2 * Pad - buttonsWidth));
        var start = Math.Max(Pad, W - Pad - text.Length - buttonsWidth);

        var x = Put(start, 0, text, new Attribute(_choices.Length > 0 ? Theme.Warn : Theme.FgDim, Theme.BgHeader));
        for (var i = 0; i < buttons.Length; i++)
        {
            x += ButtonGap;
            var end = Put(x, 0, buttons[i], Over(x, x + buttons[i].Length, 0) ? Theme.Hover : new Attribute(Theme.Bg, Theme.Accent));
            _choiceSpans.Add((x, end, _choices[i]));
            x = end;
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
        }

        return base.OnMouseEvent(mouse);
    }
}
