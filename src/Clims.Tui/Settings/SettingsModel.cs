using Clims.Tui.Ui;

namespace Clims.Tui.Settings;

/// <summary>
/// The state and logic of the settings page, without any drawing: which tab and row is selected, a text being typed, and the
/// changes themselves. Every change is saved at once through the <see cref="SettingsStore"/>.
/// </summary>
public sealed class SettingsModel
{
    private readonly SettingsStore _store;
    private readonly string? _connectedUrl;
    private string? _removeKey;

    /// <param name="store">Where the settings are kept.</param>
    /// <param name="connectedUrl">The address of the server in use (it cannot be removed), or null in demo mode.</param>
    public SettingsModel(SettingsStore store, string? connectedUrl)
    {
        _store = store;
        _connectedUrl = connectedUrl;
    }

    /// <summary>The settings were changed (and saved): the app applies what it can at once.</summary>
    public event Action<UserSettings>? Changed;

    /// <summary>The user chose a server to connect to now (its key).</summary>
    public event Action<string>? SwitchRequested;

    /// <summary>The user wants to add a server: the connect screen is wanted.</summary>
    public event Action? AddRequested;

    /// <summary>The settings as they are now.</summary>
    public UserSettings Snapshot => _store.Current;

    public IReadOnlyList<SettingsPage> Pages => SettingsPages.Build(_store.Current, _connectedUrl);

    public int PageIndex { get; private set; }

    public IReadOnlyList<SettingItem> Items => Pages[PageIndex].Items;

    /// <summary>The selected row (always one that can be selected, unless the page has only information).</summary>
    public int Selected { get; private set; }

    public SettingItem? Current => Selected >= 0 && Selected < Items.Count ? Items[Selected] : null;

    /// <summary>True while text is being typed into a row.</summary>
    public bool Editing { get; private set; }

    /// <summary>The text typed so far.</summary>
    public string Buffer { get; private set; } = "";

    /// <summary>True after a change that only takes effect at the next start.</summary>
    public bool RestartNeeded { get; private set; }

    /// <summary>A line to show under the rows: what a key did, or why it did not.</summary>
    public string? Message { get; private set; }

    /// <summary>True while the user is asked to confirm the removal of the selected server.</summary>
    public bool ConfirmingRemove => _removeKey is not null;

    public void SetPage(int index)
    {
        PageIndex = Math.Clamp(index, 0, Pages.Count - 1);
        Selected = FirstSelectable(0, 1);
        ClearTransient();
    }

    public void NextPage(int delta) => SetPage((PageIndex + delta + Pages.Count) % Pages.Count);

    /// <summary>Moves the selection, skipping rows that are only information.</summary>
    public void Move(int delta)
    {
        ClearTransient();
        var items = Items;
        for (var i = Selected + delta; i >= 0 && i < items.Count; i += delta)
        {
            if (items[i].Kind != SettingKind.Info)
            {
                Selected = i;
                return;
            }
        }
    }

    public void MoveTo(int index)
    {
        ClearTransient();
        if (index >= 0 && index < Items.Count && Items[index].Kind != SettingKind.Info)
        {
            Selected = index;
        }
    }

    /// <summary>Left (-1) or Right (+1) on a toggle, choice or number.</summary>
    public void Change(int delta)
    {
        ClearTransient();
        if (Current is { Change: { } change } item)
        {
            Apply(s => change(s, delta), item.NeedsRestart);
        }
    }

    /// <summary>Enter or Space: flips a toggle, goes to the next choice, starts typing a text, connects to a server, adds one.</summary>
    public void Activate()
    {
        ClearTransient();
        switch (Current)
        {
            case { Kind: SettingKind.Toggle or SettingKind.Choice or SettingKind.Number }:
                Change(1);
                break;
            case { Kind: SettingKind.Text }:
                BeginEdit();
                break;
            case { Kind: SettingKind.Server, ServerKey: { } key }:
                SwitchRequested?.Invoke(key);
                break;
            case { Kind: SettingKind.Action, Run: { } run }:
                Apply(run, restart: false);
                break;
            case { Kind: SettingKind.Action }:
                AddRequested?.Invoke();
                break;
        }
    }

    /// <summary>Starts typing the name of a server, or a text row.</summary>
    public void BeginEdit()
    {
        ClearTransient();
        if (Current is { EditText: { } text, Edit: not null })
        {
            Buffer = text(_store.Current);
            Editing = true;
        }
    }

    public void Type(string text)
    {
        if (Editing)
        {
            Buffer += text;
        }
    }

    public void Backspace()
    {
        if (Editing && Buffer.Length > 0)
        {
            Buffer = Buffer[..^1];
        }
    }

    public void CancelEdit()
    {
        Editing = false;
        Buffer = "";
    }

    /// <summary>Keeps the typed text. An empty text is not accepted (a name is needed).</summary>
    public void CommitEdit()
    {
        if (!Editing || Current is not { Edit: { } edit } current)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Buffer) && !current.AllowEmpty)
        {
            Message = "A name cannot be empty.";
            return;
        }

        if (current.Validate?.Invoke(Buffer) is { } problem)
        {
            Message = problem;
            return;
        }

        var text = Buffer;
        CancelEdit();
        Apply(s => edit(s, text), restart: false);
    }

    /// <summary>True on a colour row: the colour picker can be opened for it.</summary>
    public bool CanPickColor => Current is { Swatch: not null, Edit: not null };

    /// <summary>The colour of the selected colour row as it is now, or null on other rows.</summary>
    public Terminal.Gui.Drawing.Color? CurrentColor => Current?.Swatch?.Invoke(_store.Current);

    /// <summary>Sets the selected colour row to a colour chosen in the picker.</summary>
    public void SetColor(Terminal.Gui.Drawing.Color color)
    {
        ClearTransient();
        if (Current is { Swatch: not null, Edit: { } edit })
        {
            var hex = HexColor.Format(color);
            Apply(s => edit(s, hex), restart: false);
        }
    }

    /// <summary>"u" on a server: it is the one to open at the next start.</summary>
    public void UseAtStart()
    {
        ClearTransient();
        if (Current is { Kind: SettingKind.Server, ServerKey: { } key })
        {
            Apply(s => s with { Startup = s.Startup with { Server = key } }, restart: false);
            Message = "This server opens at the next start.";
        }
    }

    /// <summary>"d" on a server: asks to confirm. The server in use cannot be removed.</summary>
    public void BeginRemove()
    {
        ClearTransient();
        if (Current is not { Kind: SettingKind.Server, ServerKey: { } key } item)
        {
            return;
        }

        if (item.Connected)
        {
            Message = "This server is in use: connect to another one first.";
            return;
        }

        _removeKey = key;
        Message = $"Remove {item.Label} from the list? (y: remove, any other key: keep)";
    }

    public void ConfirmRemove()
    {
        if (_removeKey is not { } key)
        {
            return;
        }

        _removeKey = null;
        Message = null;
        Apply(s =>
        {
            var servers = s.Servers.Where(x => !x.Matches(key)).ToList();
            var start = s.Startup.Server;
            if (start is null || servers.Any(x => x.Matches(start)))
            {
                return s with { Servers = servers };
            }

            // The server that opened at start is gone: the first one left does, or none.
            var first = servers.FirstOrDefault();
            return s with { Servers = servers, Startup = s.Startup with { Server = first?.Uuid ?? first?.Url } };
        }, restart: false);
        SetPageKeepingRow();
    }

    public void CancelRemove()
    {
        _removeKey = null;
        Message = null;
    }

    private void Apply(Func<UserSettings, UserSettings> change, bool restart)
    {
        var updated = _store.Update(change);
        RestartNeeded |= restart;
        Selected = Math.Clamp(Selected, 0, Math.Max(0, Items.Count - 1));
        Changed?.Invoke(updated);
    }

    private void SetPageKeepingRow()
    {
        var keep = Selected;
        Selected = Math.Clamp(keep, 0, Math.Max(0, Items.Count - 1));
        if (Current is { Kind: SettingKind.Info })
        {
            Selected = FirstSelectable(0, 1);
        }
    }

    private int FirstSelectable(int from, int step)
    {
        var items = Items;
        for (var i = from; i >= 0 && i < items.Count; i += step)
        {
            if (items[i].Kind != SettingKind.Info)
            {
                return i;
            }
        }

        return 0;
    }

    private void ClearTransient()
    {
        _removeKey = null;
        Message = null;
    }
}
