using Clims.Lms;
using Clims.Tui.Data;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Clims.Tui.Ui;

/// <summary>
/// The Online tab: the radios and apps (Spotty, SpotOn, TIDAL, radio directories…) the server has installed. Every plugin is browsed
/// level by level the way the server's own menus work, and what is chosen is played on the player. Nothing is shown from the local
/// library here, and nothing is cached: every level is asked for when it is opened.
/// </summary>
public sealed class OnlineTab : View
{
    private const int PageSize = 100;

    private static readonly object s_more = new();

    private readonly OnlineSource? _source;
    private readonly Action<Action> _invoke;
    private readonly PromptBar _searchBar;
    private readonly KeyLegend _legend;
    private readonly List<Level> _levels = [];
    private IReadOnlyList<OnlineApp>? _apps;
    private string? _appsError;
    private bool _loadingApps;
    private int _version;
    private PendingSearch? _pending;
    private int _rootSelected = -1;
    private int _rootOffset;
    private Played? _played;

    /// <param name="source">The server's plugin browser; null (demo mode) shows that there is nothing to browse.</param>
    /// <param name="invoke">Runs an action on the UI thread: answers arrive on other threads.</param>
    public OnlineTab(OnlineSource? source, Action<Action> invoke)
    {
        _source = source;
        _invoke = invoke;
        CanFocus = true;
        Width = Dim.Fill();
        Height = Dim.Fill();

        List = new RowList { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(1) };
        _searchBar = new PromptBar { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1, Visible = false };
        _legend = new KeyLegend { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1 };
        Add(List, _legend, _searchBar);
        List.RowActivated += OnActivated;
        Render(keepPlace: false);
    }

    public RowList List { get; }

    /// <summary>True while the text of a search is being typed (letter keys then belong to the text field).</summary>
    public bool Searching => _searchBar.Visible;

    /// <summary>Something to tell the user.</summary>
    public event Action<string>? Notice;

    /// <summary>The app the last thing played from this tab came from, or null.</summary>
    public OnlineApp? PlayedApp => _played?.Path[0].App;

    /// <summary>True when the user is inside a plugin (Esc goes up a level) rather than on the list of apps.</summary>
    public bool Deep => _levels.Count > 0;

    /// <summary>The tab was opened: asks for the list of apps when it was not asked for yet.</summary>
    public void Show()
    {
        if (_apps is null && !_loadingApps)
        {
            LoadApps();
        }
    }

    /// <summary>
    /// Goes back to where the last thing played from this tab was chosen, with that item selected. When nothing was played from here
    /// (the radio was started elsewhere) the tab just opens as it was.
    /// </summary>
    public void ShowPlayed()
    {
        Show();
        if (_played is not { } played || _source is null)
        {
            return;
        }

        var same = _levels.Count == played.Path.Count
            && _levels.Zip(played.Path).All(p => p.First.App.Command == p.Second.App.Command && p.First.ItemId == p.Second.ItemId && p.First.Search == p.Second.Search);
        if (!same)
        {
            _levels.Clear();
            _levels.AddRange(played.Path.Select(l => new Level(l.App, l.ItemId, l.Search, l.Title)));
            _levels[^1].SelectId = played.ItemId;
            Reload(_levels[^1]);
            return;
        }

        SelectItem(played.ItemId);
    }

    private void SelectItem(string itemId)
    {
        var index = List.Rows.ToList().FindIndex(r => r.Data is OnlineItem item && item.Id == itemId);
        if (index >= 0)
        {
            List.SelectIndex(index);
        }
    }

    /// <summary>Forgets what was loaded and asks the server again (apps installed since, a changed login…).</summary>
    public void Reload()
    {
        _levels.Clear();
        _apps = null;
        _appsError = null;
        Render(keepPlace: false);
        LoadApps();
    }

    /// <summary>Handles the tab's keys. Returns true when the key was used.</summary>
    public bool HandleKey(Key key)
    {
        if (key == Key.Esc || key == Key.Backspace)
        {
            return GoUp();
        }

        if (Is(key, 'r'))
        {
            if (_levels.Count == 0)
            {
                Reload();
            }
            else
            {
                Reload(_levels[^1]);
            }

            return true;
        }

        // Whole lists: Shift+A adds all; Alt+A replaces the queue and plays (the footer's shuffle switch makes it random).
        if (_levels.Count > 0 && _levels[^1] is { Loading: false } open && open.Items.Any(i => i.IsAudio))
        {
            if (IsAlt(key, 'a'))
            {
                QueueAll(open, play: true);
                return true;
            }

            if (Is(key, 'A'))
            {
                QueueAll(open, play: false);
                return true;
            }
        }

        var item = List.SelectedRow?.Data as OnlineItem;
        if (Is(key, 'o') && item is not null)
        {
            Open(item);
            return true;
        }

        if (Is(key, 'a') && item is { IsAudio: true })
        {
            Queue(item, OnlineAction.Add, "Added");
            return true;
        }

        if (Is(key, 'n') && item is { IsAudio: true })
        {
            Queue(item, OnlineAction.Insert, "Playing next:");
            return true;
        }

        return false;
    }

    /// <summary>Runs the search with the typed text and closes the text bar.</summary>
    public void ConfirmSearch()
    {
        var text = _searchBar.Field.Text.Trim();
        var pending = _pending;
        CancelSearch();
        if (text.Length == 0 || pending is null)
        {
            return;
        }

        Push(new Level(pending.App, pending.ItemId, text, $"Search: {text}"));
    }

    /// <summary>Closes the text bar without searching.</summary>
    public void CancelSearch()
    {
        _pending = null;
        _searchBar.Visible = false;
        _legend.Visible = true;
        List.SetFocus();
        SetNeedsDraw();
    }

    private static bool Is(Key key, char c) => !key.IsCtrl && !key.IsAlt && key.AsRune.Value == c;

    private static bool IsAlt(Key key, char c) =>
        !key.IsCtrl && key.IsAlt && char.ToLowerInvariant((char)key.NoAlt.NoShift.AsRune.Value) == c;

    /// <summary>
    /// Queues every playable line of a level, all pages of it: added at the end, or (<paramref name="play"/>) the first one replaces the
    /// queue and starts, the others follow.
    /// </summary>
    private void QueueAll(Level level, bool play)
    {
        if (_source is null || _source.PlayerId() is not { } player)
        {
            return;
        }

        var app = level.App;
        var loaded = level.Items.ToList();
        var total = level.Total;
        var path = _levels.Select(l => new Level(l.App, l.ItemId, l.Search, l.Title)).ToList();
        var itemId = level.ItemId;
        var search = level.Search;
        Notice?.Invoke("Reading the whole list…");
        _ = Task.Run(async () =>
        {
            try
            {
                while (loaded.Count < total)
                {
                    var page = await _source.Api.BrowseAsync(player, app.Command, itemId, search, loaded.Count, PageSize).ConfigureAwait(false);
                    if (page.Items.Count == 0)
                    {
                        break;
                    }

                    loaded.AddRange(page.Items);
                }

                var tracks = loaded.Where(i => i.IsAudio).ToArray();
                if (tracks.Length == 0)
                {
                    return;
                }

                if (play)
                {
                    _invoke(() => _played = new Played(path, tracks[0].Id));
                }

                var count = tracks.Length;
                if (itemId is not null && search is null && tracks.Length > 1)
                {
                    // Plugins can queue a whole folder with one command: far quicker than a request per track. The queue length says whether it did.
                    var before = await _source.Api.QueueLengthAsync(player).ConfigureAwait(false);
                    await _source.Api.PlayAsync(player, app.Command, itemId, play ? OnlineAction.Play : OnlineAction.Add).ConfigureAwait(false);
                    var after = await _source.Api.QueueLengthAsync(player).ConfigureAwait(false);
                    var queued = play ? (after != before || after >= tracks.Length ? after : 0) : after - before;
                    if (queued > 0)
                    {
                        count = queued;
                        tracks = [];
                    }
                }

                if (tracks.Length > 0)
                {
                    var first = 0;
                    if (play)
                    {
                        await _source.Api.PlayAsync(player, app.Command, tracks[0].Id, OnlineAction.Play).ConfigureAwait(false);
                        first = 1;
                    }

                    for (var i = first; i < tracks.Length; i++)
                    {
                        await _source.Api.PlayAsync(player, app.Command, tracks[i].Id, OnlineAction.Add).ConfigureAwait(false);
                    }
                }

                var what = count == 1 ? "1 track" : $"{count} tracks";
                _invoke(() => Notice?.Invoke((play ? "Playing " : "Added ") + what));
            }
            catch (Exception ex) when (ex is LmsException or HttpRequestException or TaskCanceledException)
            {
                _invoke(() => Notice?.Invoke("The server could not do that: " + ex.Message));
            }
        });
    }

    private bool GoUp()
    {
        if (_levels.Count == 0)
        {
            return false;
        }

        _levels.RemoveAt(_levels.Count - 1);
        _version++;
        if (_levels.Count > 0 && _levels[^1].Items.Count == 0 && !_levels[^1].Loading)
        {
            // A level that was never loaded (the path to something played was rebuilt): ask for it now.
            Reload(_levels[^1]);
            return true;
        }

        var (selected, offset) = _levels.Count == 0 ? (_rootSelected, _rootOffset) : (_levels[^1].Selected, _levels[^1].Offset);
        Render(keepPlace: false);
        List.Offset = offset;
        if (selected >= 0 && selected < List.Rows.Count && List.Rows[selected].Kind == RowKind.Item)
        {
            List.SelectIndex(selected);
        }

        return true;
    }

    private void OnActivated(Row row)
    {
        switch (row.Data)
        {
            case OnlineApp app:
                if (app.IsSearch)
                {
                    BeginSearch(app, null);
                }
                else
                {
                    Push(new Level(app, null, null, app.Name));
                }

                break;
            case OnlineItem { IsAudio: true } item:
                Queue(item, OnlineAction.Play, "Playing");
                break;
            case OnlineItem item:
                Open(item);
                break;
            case var more when ReferenceEquals(more, s_more):
                LoadMore();
                break;
        }
    }

    private void Open(OnlineItem item)
    {
        if (item.IsSearch)
        {
            BeginSearch(_levels[^1].App, item.Id);
        }
        else if (item.HasItems)
        {
            Push(new Level(_levels[^1].App, item.Id, null, item.Name));
        }
    }

    private void BeginSearch(OnlineApp app, string? itemId)
    {
        _pending = new PendingSearch(app, itemId);
        _searchBar.Prompt = $"Search {app.Name}:";
        _searchBar.Field.Text = "";
        _searchBar.Visible = true;
        _legend.Visible = false;
        _searchBar.Field.SetFocus();
        SetNeedsDraw();
    }

    private void Push(Level level)
    {
        if (_levels.Count > 0)
        {
            _levels[^1].Selected = List.SelectedIndex;
            _levels[^1].Offset = List.Offset;
        }
        else
        {
            _rootSelected = List.SelectedIndex;
            _rootOffset = List.Offset;
        }

        _levels.Add(level);
        Reload(level);
    }

    /// <summary>Asks for the first page of a level again.</summary>
    private void Reload(Level level)
    {
        level.Items.Clear();
        level.Total = 0;
        level.Error = null;
        level.Loading = true;
        level.Selected = -1;
        Render(keepPlace: false);
        Fetch(level, 0);
    }

    private void LoadMore()
    {
        if (_levels.Count == 0 || _levels[^1] is not { Loading: false } level)
        {
            return;
        }

        // The list is left untouched while the next page is on its way (re-showing it, even with "Loading…", moved the view to the top).
        level.Loading = true;
        Notice?.Invoke("Loading more…");
        Fetch(level, level.Items.Count);
    }

    private void Fetch(Level level, int start)
    {
        if (_source is null || _source.PlayerId() is not { } player)
        {
            level.Loading = false;
            level.Error = "No player to ask for the list yet.";
            Render(keepPlace: true);
            return;
        }

        var version = ++_version;
        _ = Task.Run(async () =>
        {
            try
            {
                var listing = await _source.Api.BrowseAsync(player, level.App.Command, level.ItemId, level.Search, start, PageSize).ConfigureAwait(false);
                _invoke(() => Arrived(level, version, listing, null));
            }
            catch (Exception ex) when (ex is LmsException or HttpRequestException or TaskCanceledException)
            {
                _invoke(() => Arrived(level, version, null, ex.Message));
            }
        });
    }

    private void Arrived(Level level, int version, OnlineListing? listing, string? error)
    {
        if (version != _version || !_levels.Contains(level))
        {
            return;     // the user went elsewhere in the meantime
        }

        var more = level.Items.Count > 0;
        var firstNew = level.Items.Count + 1;   // the header takes line 0
        var (offset, selected) = (List.Offset, List.SelectedIndex);
        level.Loading = false;
        level.Error = error;
        if (listing is not null)
        {
            level.Items.AddRange(listing.Items);
            level.Total = listing.Total;
            if (level.Title.Length == 0 && listing.Title.Length > 0)
            {
                level.Title = listing.Title;
            }
        }

        Render(keepPlace: more);
        if (more && listing is not null && selected == firstNew)
        {
            // "Load more" was selected: stay where it was and select the first line that arrived, instead of jumping to the top.
            List.Offset = offset;
            if (firstNew < List.Rows.Count && List.Rows[firstNew].Kind == RowKind.Item)
            {
                List.SelectIndex(firstNew);
            }
        }

        if (level.SelectId is { } wanted)
        {
            level.SelectId = null;
            SelectItem(wanted);
        }
    }

    private void LoadApps()
    {
        if (_source is null || _source.PlayerId() is not { } player)
        {
            Render(keepPlace: false);
            return;
        }

        _loadingApps = true;
        Render(keepPlace: false);
        _ = Task.Run(async () =>
        {
            try
            {
                var apps = await _source.Api.GetAppsAsync(player).ConfigureAwait(false);
                _invoke(() => AppsArrived(apps, null));
            }
            catch (Exception ex) when (ex is LmsException or HttpRequestException or TaskCanceledException)
            {
                _invoke(() => AppsArrived(null, ex.Message));
            }
        });
    }

    private void AppsArrived(IReadOnlyList<OnlineApp>? apps, string? error)
    {
        _loadingApps = false;
        _apps = apps;
        _appsError = error;
        if (_levels.Count == 0)
        {
            Render(keepPlace: false);
        }
    }

    private void Queue(OnlineItem item, OnlineAction action, string verb)
    {
        if (_source is null || _source.PlayerId() is not { } player || _levels.Count == 0)
        {
            return;
        }

        if (action == OnlineAction.Play)
        {
            _played = new Played(_levels.Select(l => new Level(l.App, l.ItemId, l.Search, l.Title)).ToList(), item.Id);
        }

        var command = _levels[^1].App.Command;
        _ = Task.Run(async () =>
        {
            try
            {
                await _source.Api.PlayAsync(player, command, item.Id, action).ConfigureAwait(false);
                _invoke(() => Notice?.Invoke($"{verb} {item.Name}"));
            }
            catch (Exception ex) when (ex is LmsException or HttpRequestException or TaskCanceledException)
            {
                _invoke(() => Notice?.Invoke("The server could not do that: " + ex.Message));
            }
        });
    }

    /// <summary>Shows the current level. With <paramref name="keepPlace"/> the selection and scroll position stay where they were (more lines arrived, or the user came back up).</summary>
    private void Render(bool keepPlace)
    {
        var keep = List.SelectedIndex;
        var scroll = List.Offset;
        List.Rows = _levels.Count == 0 ? RootRows() : LevelRows(_levels[^1]);
        if (keepPlace)
        {
            List.Offset = scroll;
            if (keep >= 0 && keep < List.Rows.Count && List.Rows[keep].Kind == RowKind.Item)
            {
                List.SelectIndex(keep);
            }
        }

        _legend.Items = _levels.Count == 0
            ? [("Enter", "open"), ("r", "reload")]
            : _levels[^1].Items.Any(i => i.IsAudio)
                ? [("Enter", "play / open"), ("o", "open"), ("a", "add"), ("n", "play next"), ("A", "add all"), ("Alt+A", "play all"), ("Esc", "back"), ("r", "reload")]
                : [("Enter", "play / open"), ("o", "open"), ("a", "add"), ("n", "play next"), ("Esc", "back"), ("r", "reload")];
        SetNeedsDraw();
    }

    private List<Row> RootRows()
    {
        var rows = new List<Row>();
        if (_source is null)
        {
            rows.Add(new Row("Online", Kind: RowKind.Header));
            rows.Add(new Row("Radio and apps need a server: nothing to show in demo mode.", Kind: RowKind.More));
            return rows;
        }

        if (_apps is null)
        {
            rows.Add(new Row("Online", Kind: RowKind.Header));
            rows.Add(new Row(_appsError is null ? "Asking the server…" : "The server could not be asked: " + _appsError + "  (r to try again)", Kind: RowKind.More));
            return rows;
        }

        if (_apps.Count == 0)
        {
            rows.Add(new Row("Online", Kind: RowKind.Header));
            rows.Add(new Row("The server has no radio or app plugins installed.", Kind: RowKind.More));
            return rows;
        }

        foreach (var (title, radio) in new[] { ("Radio", true), ("Apps", false) })
        {
            var group = _apps.Where(a => a.IsRadio == radio).ToList();
            if (group.Count == 0)
            {
                continue;
            }

            rows.Add(new Row(title, Kind: RowKind.Header));
            rows.AddRange(group.Select(a => new Row(a.Name, a.IsSearch ? "search" : "›", Data: a)));
        }

        return rows;
    }

    private static List<Row> LevelRows(Level level)
    {
        var rows = new List<Row> { new(level.Crumb, Kind: RowKind.Header) };
        foreach (var item in level.Items)
        {
            var right = item.IsAudio && item.HasItems ? "▶ ›" : item.IsAudio ? "▶" : item.IsSearch ? "search" : item.HasItems ? "›" : "";
            rows.Add(item.IsText ? new Row(item.Name, Kind: RowKind.More) : new Row(item.Name, right, Data: item));
        }

        if (level.Loading)
        {
            rows.Add(new Row("Loading…", Kind: RowKind.More));
        }
        else if (level.Error is not null)
        {
            rows.Add(new Row("The server could not be asked: " + level.Error + "  (r to try again)", Kind: RowKind.More));
        }
        else if (level.Items.Count == 0)
        {
            rows.Add(new Row("Nothing here.", Kind: RowKind.More));
        }
        else if (level.Total > level.Items.Count)
        {
            rows.Add(new Row($"Load more… ({level.Total - level.Items.Count} left)", Data: s_more));
        }

        return rows;
    }

    private sealed record PendingSearch(OnlineApp App, string? ItemId);

    /// <summary>Where the last thing played from this tab was chosen: the levels opened to get there and the item.</summary>
    private sealed record Played(List<Level> Path, string ItemId);

    /// <summary>One opened level of one app.</summary>
    private sealed class Level
    {
        public Level(OnlineApp app, string? itemId, string? search, string title)
        {
            App = app;
            ItemId = itemId;
            Search = search;
            Title = title;
        }

        public OnlineApp App { get; }

        public string? ItemId { get; }

        public string? Search { get; }

        public string Title { get; set; }

        public List<OnlineItem> Items { get; } = [];

        public int Total { get; set; }

        public bool Loading { get; set; }

        public string? Error { get; set; }

        public int Selected { get; set; } = -1;

        public int Offset { get; set; }

        /// <summary>The item to select once the level has arrived (coming back to what was played).</summary>
        public string? SelectId { get; set; }

        public string Crumb => Title.Length > 0 && Title != App.Name ? $"{App.Name} › {Title}" : App.Name;
    }
}
