using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>The key reference, drawn over the tabs when <c>?</c> is pressed (any tab, any time the search box is not being typed in).</summary>
public sealed class HelpOverlay : PaintView
{
    private static readonly (string Keys, string What)[] Lines =
    {
        ("Everywhere", ""),
        ("/", "Search (type; Esc clears and goes back; Enter or Down moves to the results)"),
        ("1  2  3  4  5", "Search / Library / Queue / Recently added / Genres tab (4 again: ask the server for the newest albums)"),
        ("Space", "Play / pause"),
        ("Left  Right", "Seek -10 s / +10 s (or click the progress bar)"),
        ("Alt+n  Alt+p", "Next / previous track, from anywhere (previous restarts the track after 3 s)"),
        ("+  -", "Volume"),
        ("F2  ⇄ Server", "Connect to another server or enter a login (button at the top right)"),
        ("F5", "Resync the library, or cancel a running sync"),
        ("F3  F4", "Cover rendering mode / list cover size"),
        ("?  Ctrl+Q", "This help / Quit"),
        ("Library and search results", ""),
        ("Enter  double click", "Play (replaces the queue, restarts the stream), open an artist or genre; a single click only selects"),
        ("Right click", "Menu on a cover or track: Play, Enqueue"),
        ("a  n", "Add the highlighted track to the end of the queue / play it next (gapless)"),
        ("A  N", "The same for the whole album (Library and Recently added tabs)"),
        ("F6", "Library view: Album Artist / Genre / Year"),
        ("Album view (click the player's cover)", ""),
        ("Up  Down  PgUp  PgDn", "Scroll the lyrics (the mouse wheel too); synced lyrics follow the music again after 8 s"),
        ("[  ]", "Shift the timing of synced lyrics by -0.5 s / +0.5 s"),
        ("R  L", "Search the lyrics again / turn online lookup on (it sends artist, album, title and length to the providers)"),
        ("Queue tab", ""),
        ("Enter", "Play that entry"),
        ("x  Delete", "Remove the entry"),
        ("Shift+Up  Shift+Down", "Move the entry"),
        ("c", "Clear the queue (asks first)"),
        ("s", "Save the queue under a name (kept on this PC, by file address)"),
        ("o", "Saved queues: Enter replaces the queue (asks first), a appends, x deletes, Esc back"),
        ("Genres tab", ""),
        ("Up  Down  Enter", "Pick a genre: the next column lists the genres that share a track with it; Enter or Right goes there, Left or Backspace back"),
        ("Ctrl+F  Esc", "Filter the active column (Enter keeps it, Esc clears it); Esc with no filter clears the whole chain"),
        ("Alt+R  Alt+Q", "Random queue of the tagged tracks: replace the queue (asks first) / append"),
        ("[  ]", "Random queue size: 25, 50 or 100 tracks (or click it)"),
    };

    private const string Note = "Playing an album from the library restarts the stream; adding to the queue keeps playback gapless.";

    private int _offset;

    public HelpOverlay()
    {
        // Focusable, so that hiding the pages under it does not hand the keyboard to the search box.
        CanFocus = true;
    }

    protected override Attribute Base => new(Theme.Fg, Theme.BgAlt);

    /// <summary>Back to the top, for when the help is opened again.</summary>
    public void ScrollToTop() => _offset = 0;

    /// <summary>Rows that fit under the title.</summary>
    private int Room => Math.Max(1, H - 3);

    /// <summary>What is drawn: every line, a blank row before each section title but the first, and the closing note.</summary>
    private static int RowCount => Lines.Length + Lines.Count(l => l.What.Length == 0) - 1 + 1;

    protected override void Paint()
    {
        var attr = new Attribute(Theme.Fg, Theme.BgAlt);
        var head = new Attribute(Theme.Accent, Theme.BgAlt);
        var dim = new Attribute(Theme.FgDim, Theme.BgAlt);
        var scrolls = RowCount > Room;
        _offset = Math.Clamp(_offset, 0, Math.Max(0, RowCount - Room));
        Put(2, 1, "cLiMS keys   (press ? or Esc to close)" + (scrolls ? "   ↑ ↓ PgUp PgDn scroll" : ""), head);

        var row = 0;
        void Emit(Action<int> draw)
        {
            var y = 3 + row - _offset;
            if (y >= 3 && y < 3 + Room)
            {
                draw(y);
            }

            row++;
        }

        foreach (var (keys, what) in Lines)
        {
            if (what.Length == 0)
            {
                if (row > 0)
                {
                    Emit(_ => { });
                }

                Emit(y => Put(2, y, keys, head));
                continue;
            }

            Emit(y =>
            {
                Put(4, y, keys, new Attribute(Theme.Warn, Theme.BgAlt));
                Put(26, y, Clip(what, W - 28), attr);
            });
        }

        Emit(y => Put(2, y, Clip(Note, W - 4), dim));
        PaintScrollbar(RowCount, _offset, Room);
    }

    protected override bool OnKeyDown(Key key)
    {
        var page = Math.Max(1, Room - 1);
        switch (key.KeyCode)
        {
            case KeyCode.CursorDown:
                Scroll(1);
                return true;
            case KeyCode.CursorUp:
                Scroll(-1);
                return true;
            case KeyCode.PageDown:
                Scroll(page);
                return true;
            case KeyCode.PageUp:
                Scroll(-page);
                return true;
            case KeyCode.Home:
                Scroll(-RowCount);
                return true;
            case KeyCode.End:
                Scroll(RowCount);
                return true;
        }

        return base.OnKeyDown(key);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
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

        return base.OnMouseEvent(mouse);
    }

    private void Scroll(int delta)
    {
        _offset = Math.Clamp(_offset + delta, 0, Math.Max(0, RowCount - Room));
        SetNeedsDraw();
    }
}
