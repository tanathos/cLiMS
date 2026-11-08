using Clims.Catalog;
using Clims.Tui.Data;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Clims.Tui.Ui;

/// <summary>Three rows tall: a blank row above and below the content row, and two columns of padding on each side.</summary>
public sealed class HeaderBar : View
{
    public const int Rows = 3;
    private const int Pad = 2;
    private const int SearchWidth = 44;
    private const int ServerButtonWidth = 8; // exactly the width of "⇄ Server"

    private readonly StatusText _status;

    public HeaderBar(IPlayerController player, Func<SyncStatus> syncStatus)
    {
        CanFocus = false; // a click on the bar's empty cells must not move focus to the search box
        Height = Rows;
        Width = Dim.Fill();

        var brand = new BrandText { X = Pad, Y = 1, Width = 8, Height = 1 };
        Search = new TextField { X = Pad + 9, Y = 1, Width = SearchWidth, Height = 1 };
        _status = new StatusText(player, syncStatus) { X = Pad + 9 + SearchWidth + 2, Y = 1, Width = Dim.Fill(Pad + ServerButtonWidth + 1), Height = 1 };
        var server = new ServerButton { X = Pos.AnchorEnd(Pad + ServerButtonWidth), Y = 1, Width = ServerButtonWidth, Height = 1 };
        server.Clicked += () => ServerClicked?.Invoke();
        _status.Clicked += () => SyncClicked?.Invoke();
        Add(new Backdrop { X = 0, Y = 0, Width = Dim.Fill(), Height = Rows });
        Add(brand, _status, server);
    }

    /// <summary>The search box. It is not a child of the bar (a focusable parent would pull focus to it on any click in the bar): the main window adds it next to the bar.</summary>
    public TextField Search { get; }

    /// <summary>The "Server" button was clicked: open the connect screen.</summary>
    public event Action? ServerClicked;

    /// <summary>The sync status text was clicked: offer "Resync now" or "Cancel".</summary>
    public event Action? SyncClicked;

    public void Refresh() => _status.SetNeedsDraw();

    /// <summary>Symbol, text and colour for a sync status (the five states of search.md section 10, plus demo).</summary>
    public static (string Symbol, string Label, Color Color) Describe(SyncStatus s, DateTimeOffset now) => s.State switch
    {
        SyncState.Syncing => ("◐", "Sync in progress, live search only" + (s.Progress is { Length: > 0 } p ? " · " + p : ""), Theme.Warn),
        SyncState.Synced => ("●", $"Synced ({Age(s.SyncedAt, now)})", Theme.Good),
        SyncState.ServerChanged => ("◔", "Server changed, resync pending", Theme.Warn),
        SyncState.Offline => ("✕", s.HasIndex ? "Offline (using local index)" : "Offline", Theme.Bad),
        SyncState.LoginRequired => ("✕", "Login required (F2)", Theme.Bad),
        SyncState.Demo => ("◇", "Demo data (no server)", Theme.Accent),
        _ => ("○", "Not synced", Theme.Warn),
    };

    /// <summary>Compact age such as "just now", "5 min ago", "2 h ago", "3 d ago".</summary>
    public static string Age(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is null)
        {
            return "unknown age";
        }

        var d = now - at.Value;
        return d.TotalMinutes < 1 ? "just now"
            : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min ago"
            : d.TotalDays < 1 ? $"{(int)d.TotalHours} h ago"
            : $"{(int)d.TotalDays} d ago";
    }

    private static Attribute OnHeader(Color fg) => new(fg, Theme.BgHeader);

    private sealed class Backdrop : PaintView
    {
        protected override Attribute Base => OnHeader(Theme.Fg);

        protected override void Paint()
        {
        }
    }

    private sealed class ServerButton : PaintView
    {
        protected override Attribute Base => OnHeader(Theme.Fg);

        public event Action? Clicked;

        protected override void Paint() => Put(0, 0, "⇄ Server", new Attribute(Theme.Fg, Theme.BgAlt));

        protected override bool OnMouseEvent(Mouse mouse)
        {
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
            {
                Clicked?.Invoke();
                return true;
            }

            return base.OnMouseEvent(mouse);
        }
    }

    private sealed class BrandText : PaintView
    {
        protected override Attribute Base => OnHeader(Theme.Fg);

        protected override void Paint() => Put(0, 0, "cLiMS /", OnHeader(Theme.Accent));
    }

    private sealed class StatusText : PaintView
    {
        private readonly IPlayerController _player;
        private readonly Func<SyncStatus> _status;

        public StatusText(IPlayerController player, Func<SyncStatus> status)
        {
            _player = player;
            _status = status;
        }

        private (int X0, int X1) _icon;

        public event Action? Clicked;

        protected override Attribute Base => OnHeader(Theme.Fg);

        protected override void Paint()
        {
            // Right-aligned "name ●": only the icon is clickable, the status text is shown when it is clicked.
            var (symbol, _, color) = Describe(_status(), DateTimeOffset.UtcNow);
            var name = _player.PlayerName + " ";
            var x = Math.Max(0, W - name.Length - symbol.Length);
            x = Put(x, 0, name, OnHeader(Theme.Fg));
            _icon = (x, x + symbol.Length);
            Put(x, 0, symbol, OnHeader(color));
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && mouse.Position is { } p && p.Y == 0 && p.X >= _icon.X0 && p.X < _icon.X1)
            {
                Clicked?.Invoke();
                return true;
            }

            return base.OnMouseEvent(mouse);
        }
    }
}
