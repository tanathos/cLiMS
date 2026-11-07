namespace Clims.Tui.Ui;

/// <summary>The key reference, drawn over the tabs when <c>?</c> is pressed (any tab, any time the search box is not being typed in).</summary>
public sealed class HelpOverlay : PaintView
{
    private static readonly (string Keys, string What)[] Lines =
    {
        ("Everywhere", ""),
        ("/", "Search (type; Esc clears and goes back; Enter or Down moves to the results)"),
        ("1  2  3  4", "Search / Library / Queue / Recently added tab (4 again: ask the server for the newest albums)"),
        ("Space", "Play / pause"),
        ("Left  Right", "Seek -10 s / +10 s (or click the progress bar)"),
        ("Alt+n  Alt+p", "Next / previous track, from anywhere (previous restarts the track after 3 s)"),
        ("+  -", "Volume"),
        ("F2  ⇄ Server", "Connect to another server or enter a login (button at the top right)"),
        ("F5", "Resync the library, or cancel a running sync"),
        ("F3  F4", "Cover rendering mode / list cover size"),
        ("?", "This help"),
        ("Ctrl+Q", "Quit"),
        ("Library and search results", ""),
        ("Enter  double click", "Play (replaces the queue, restarts the stream), open an artist or genre; a single click only selects"),
        ("a  n", "Add the highlighted track to the end of the queue / play it next (gapless)"),
        ("A  N", "The same for the whole album (Library and Recently added tabs)"),
        ("F6", "Library view: Album Artist / Genre / Year"),
        ("Queue tab", ""),
        ("Enter", "Play that entry"),
        ("x  Delete", "Remove the entry"),
        ("Shift+Up  Shift+Down", "Move the entry"),
        ("c", "Clear the queue (asks first)"),
        ("s", "Save the queue under a name (kept on this PC, by file address)"),
        ("o", "Saved queues: Enter replaces the queue (asks first), a appends, x deletes, Esc back"),
    };

    public HelpOverlay()
    {
        // Focusable, so that hiding the pages under it does not hand the keyboard to the search box.
        CanFocus = true;
    }

    protected override Attribute Base => new(Theme.Fg, Theme.BgAlt);

    protected override void Paint()
    {
        var attr = new Attribute(Theme.Fg, Theme.BgAlt);
        var head = new Attribute(Theme.Accent, Theme.BgAlt);
        var dim = new Attribute(Theme.FgDim, Theme.BgAlt);
        var y = 1;
        Put(2, y++, "cLiMS keys   (press ? or Esc to close)", head);
        y++;
        foreach (var (keys, what) in Lines)
        {
            if (y >= H - 1)
            {
                break;
            }

            if (what.Length == 0)
            {
                y++;
                Put(2, y++, keys, head);
                continue;
            }

            Put(4, y, keys, new Attribute(Theme.Warn, Theme.BgAlt));
            Put(26, y, Clip(what, W - 28), attr);
            y++;
        }

        Put(2, Math.Min(H - 1, y + 1), "Playing an album from the library restarts the stream; adding to the queue keeps playback gapless.", dim);
    }
}
