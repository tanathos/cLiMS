using Clims.Lms;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Clims.Tui.Ui;

/// <summary>
/// The "Connect to a server" screen: servers found on the network (to pick from) and, always, a form for a specific
/// address with optional username and password. The server is only used once the user confirmed it and the check passed.
/// </summary>
public sealed class ConnectWindow : Window
{
    private const int ListY = 5;
    private const int ListRows = 5;
    private const int FormY = 12;
    private const int LabelX = 3;
    private const int FieldX = 14;
    private const int ButtonY = 18;

    private readonly IApplication _app;
    private readonly ConnectModel _model;
    private readonly bool _allowDemo;
    private readonly CancellationTokenSource _cancel = new();
    private readonly TextField _address;
    private readonly TextField _user;
    private readonly TextField _password;
    private readonly Form _form;
    private int _selected = -1;

    /// <summary>Creates the screen.</summary>
    /// <param name="app">The running application.</param>
    /// <param name="model">The logic (real or faked network).</param>
    /// <param name="allowDemo">First run: Esc starts demo mode. Otherwise Esc cancels and the running server is kept.</param>
    /// <param name="address">Address to start with, or null.</param>
    /// <param name="username">Username to start with, or null.</param>
    public ConnectWindow(IApplication app, ConnectModel model, bool allowDemo, string? address = null, string? username = null)
    {
        _app = app;
        _model = model;
        _allowDemo = allowDemo;
        Title = "";
        BorderStyle = LineStyle.None;
        SetScheme(Theme.Dark);

        _form = new Form(this) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _address = new TextField { X = FieldX, Y = FormY + 1, Width = 40, Height = 1, Text = address ?? "" };
        _user = new TextField { X = FieldX, Y = FormY + 2, Width = 40, Height = 1, Text = username ?? "" };
        _password = new TextField { X = FieldX, Y = FormY + 3, Width = 40, Height = 1, Secret = true };
        Add(_form, _address, _user, _password);

        model.Changed += () => _app.Invoke(() => _form.SetNeedsDraw());
        model.FirstServerFound += server => _app.Invoke(() =>
        {
            _selected = 0;
            if (_address.Text.Length == 0)
            {
                Offer(server);
            }
        });

        // Esc quits by default and the screen uses it: take that binding away, as the main window does.
        if (app.Keyboard.KeyBindings.TryGet(Key.Esc, out _))
        {
            app.Keyboard.KeyBindings.Remove(Key.Esc);
        }

        app.Keyboard.KeyDown += OnKey;
        app.AddTimeout(TimeSpan.FromMilliseconds(50), () =>
        {
            _ = Task.Run(() => _model.DiscoverAsync(_cancel.Token));
            return false;
        });
        (_address.Text.Length == 0 ? _address : _password).SetFocus();
    }

    /// <summary>How it ended; set before the screen closes.</summary>
    public ConnectResult Outcome { get; private set; } = new(ConnectChoice.Cancelled);

    /// <summary>Shows the screen and returns what the user decided.</summary>
    public static ConnectResult Show(IApplication app, bool allowDemo, string? address = null, string? username = null, ConnectModel? model = null)
    {
        using var window = new ConnectWindow(app, model ?? new ConnectModel(), allowDemo, address, username);
        app.Run(window);
        return window.Outcome;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _app.Keyboard.KeyDown -= OnKey;
            _cancel.Cancel();
            _cancel.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Offer(DiscoveredServer server) =>
        _address.Text = server.WebPort == ConnectModel.DefaultPort ? server.Host : $"{server.Host}:{server.WebPort}";

    private void Select(int index)
    {
        var servers = _model.Servers;
        if (servers.Count == 0)
        {
            return;
        }

        _selected = Math.Clamp(index, 0, servers.Count - 1);
        Offer(servers[_selected]);
        _form.SetNeedsDraw();
    }

    private void Finish(ConnectResult result)
    {
        Outcome = result;
        _app.RequestStop();
    }

    private void Escape() => Finish(new ConnectResult(_allowDemo ? ConnectChoice.Demo : ConnectChoice.Cancelled));

    private async void ConnectNow()
    {
        var result = await _model.ConnectAsync(_address.Text, _user.Text, _password.Text, _cancel.Token).ConfigureAwait(false);
        _app.Invoke(() =>
        {
            if (result is not null)
            {
                Finish(result);
            }
            else if (_model.LastOutcome == ProbeOutcome.LoginRequired)
            {
                // The server asked for a login: take the user to the field that is missing or wrong.
                (_user.Text.Length == 0 ? _user : _password).SetFocus();
            }
            else if (_model.LastOutcome is not null || _address.Text.Length == 0)
            {
                _address.SetFocus();
            }
        });
    }

    private void OnKey(object? sender, Key key)
    {
        if (key == Key.Q.WithCtrl)
        {
            Outcome = new ConnectResult(ConnectChoice.Cancelled);
            _app.RequestStop();
        }
        else if (key == Key.Esc)
        {
            Escape();
        }
        else if (key == Key.Enter)
        {
            ConnectNow();
        }
        else if (key == Key.CursorDown)
        {
            Select(_selected + 1);
        }
        else if (key == Key.CursorUp)
        {
            Select(_selected - 1);
        }
        else
        {
            return;
        }

        key.Handled = true;
    }

    /// <summary>Everything that is not a text field: headings, the server list, messages and the two buttons.</summary>
    private sealed class Form : PaintView
    {
        private readonly ConnectWindow _w;

        public Form(ConnectWindow w) => _w = w;

        private string ConnectLabel => _w._model.Busy ? "[ Connecting… ]" : "[ Connect ]";

        private string EscapeLabel => _w._allowDemo ? "[ Demo ]" : "[ Cancel ]";

        protected override void Paint()
        {
            var head = new Attribute(Theme.Accent, Theme.Bg);
            var dim = new Attribute(Theme.FgDim, Theme.Bg);
            var text = Theme.Normal;
            Put(LabelX, 1, "cLiMS · Connect to a Lyrion Music Server", head);
            Put(LabelX, 3, "Servers on your network", head);
            Put(LabelX, 4, _w._model.SearchLine, dim);

            var servers = _w._model.Servers;
            for (var i = 0; i < Math.Min(servers.Count, ListRows); i++)
            {
                var s = servers[i];
                var on = i == _w._selected;
                var attr = on ? Theme.Selected : text;
                FillRow(ListY + i, LabelX - 1, Math.Min(W, LabelX + 70), attr);
                var line = $"{s.Name}  ·  {s.Host}:{s.WebPort}" + (s.Version is { Length: > 0 } v ? $"  ·  {v}" : "");
                Put(LabelX + 1, ListY + i, Clip(line, 66), attr);
            }

            Put(LabelX, FormY, "Connect to a specific address", head);
            Put(LabelX, FormY + 1, "Address", text);
            Put(LabelX, FormY + 2, "Username", text);
            Put(LabelX, FormY + 3, "Password", text);
            Put(FieldX + 42, FormY + 1, "host, host:port or URL", dim);
            Put(FieldX + 42, FormY + 2, "only if the server asks", dim);

            if (_w._model.Message is { Length: > 0 } message)
            {
                Put(LabelX, FormY + 5, Clip(message, W - LabelX - 2), new Attribute(_w._model.MessageIsError ? Theme.Bad : Theme.Good, Theme.Bg));
            }

            var button = new Attribute(Theme.Fg, Theme.BgSelected);
            var other = new Attribute(Theme.Fg, Theme.BgAlt);
            Put(FieldX, ButtonY, ConnectLabel, button);
            Put(FieldX + ConnectLabel.Length + 2, ButtonY, EscapeLabel, other);
            Put(LabelX, ButtonY + 2, _w._allowDemo
                ? "Tab next field · Enter connect · Esc demo mode (nothing is saved) · Ctrl+Q quit"
                : "Tab next field · Enter connect · Esc keep the current server", dim);
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            var double_ = mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked);
            if (!double_ && !mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) || mouse.Position is not { } p)
            {
                return base.OnMouseEvent(mouse);
            }

            var servers = _w._model.Servers;
            var row = p.Y - ListY;
            if (row >= 0 && row < Math.Min(servers.Count, ListRows))
            {
                _w.Select(row);
                if (double_)
                {
                    _w.ConnectNow();
                }

                return true;
            }

            if (p.Y == ButtonY)
            {
                if (p.X >= FieldX && p.X < FieldX + ConnectLabel.Length)
                {
                    _w.ConnectNow();
                    return true;
                }

                var x2 = FieldX + ConnectLabel.Length + 2;
                if (p.X >= x2 && p.X < x2 + EscapeLabel.Length)
                {
                    _w.Escape();
                    return true;
                }
            }

            return base.OnMouseEvent(mouse);
        }
    }
}
