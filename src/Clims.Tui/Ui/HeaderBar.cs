using Clims.Tui.Data;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Clims.Tui.Ui;

/// <summary>Three rows tall: a blank row above and below the content row, and two columns of padding on each side.</summary>
public sealed class HeaderBar : View
{
    public const int Rows = 3;
    private const int Pad = 2;
    private const int SearchWidth = 44;

    private readonly StatusText _status;

    public HeaderBar(IPlayerController player)
    {
        CanFocus = true;
        Height = Rows;
        Width = Dim.Fill();

        var brand = new BrandText { X = Pad, Y = 1, Width = 8, Height = 1 };
        Search = new TextField { X = Pad + 9, Y = 1, Width = SearchWidth, Height = 1 };
        _status = new StatusText(player) { X = Pad + 9 + SearchWidth + 2, Y = 1, Width = Dim.Fill(Pad), Height = 1 };
        Add(new Backdrop { X = 0, Y = 0, Width = Dim.Fill(), Height = Rows });
        Add(brand, Search, _status);
    }

    public TextField Search { get; }

    public SyncState Sync
    {
        get => _status.Sync;
        set => _status.Sync = value;
    }

    public void Refresh() => _status.SetNeedsDraw();

    private static Attribute OnHeader(Terminal.Gui.Drawing.Color fg) => new(fg, Theme.BgHeader);

    private sealed class Backdrop : PaintView
    {
        protected override Attribute Base => OnHeader(Theme.Fg);

        protected override void Paint()
        {
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

        public StatusText(IPlayerController player)
        {
            _player = player;
        }

        public SyncState Sync { get; set; } = SyncState.Synced;

        protected override Attribute Base => OnHeader(Theme.Fg);

        protected override void Paint()
        {
            var (label, color) = Sync switch
            {
                SyncState.NotSynced => ("Not synced", Theme.Warn),
                SyncState.InProgress => ("Sync in progress, live search only", Theme.Warn),
                _ => ("Synced (2 h ago)", Theme.Good),
            };

            // Right-aligned. The emoji is two UTF-16 chars but one display cell pair, so it is counted as 2.
            var right = $"  🔊 {_player.Volume}";
            var text = $"{_player.PlayerName} ● {label}";
            var x = Math.Max(0, W - text.Length - right.Length);
            x = Put(x, 0, _player.PlayerName + " ", OnHeader(Theme.Fg));
            x = Put(x, 0, "● ", OnHeader(color));
            x = Put(x, 0, label, OnHeader(Theme.FgDim));
            Put(x, 0, right, OnHeader(Theme.Fg));
        }
    }
}
