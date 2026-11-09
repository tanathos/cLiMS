using Clims.Tui.Data;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Clims.Tui.Ui;

/// <summary>
/// The Queue tab: the player's live queue (jump, remove, reorder, clear) and the queues saved on this PC (save the current
/// one under a name, load one, append one, delete one). Saved queues remember files by URL because track ids change
/// after a rescan.
/// </summary>
public sealed class QueueTab : View
{
    private readonly IPlayerController _player;
    private readonly SavedQueueStore _store;
    private readonly NameBar _nameBar;
    private bool _showSaved;
    private string _signature = "";

    public QueueTab(IPlayerController player, SavedQueueStore store)
    {
        _player = player;
        _store = store;
        CanFocus = true;
        Width = Dim.Fill();
        Height = Dim.Fill();

        List = new RowList { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(1) };
        _nameBar = new NameBar { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1, Visible = false };
        Add(List, _nameBar);
        List.RowActivated += OnActivated;
        Rebuild(force: true);
    }

    public RowList List { get; }

    /// <summary>True while the name of a queue to save is being typed (letter keys then belong to the text field).</summary>
    public bool Naming => _nameBar.Visible;

    /// <summary>Something to tell the user.</summary>
    public event Action<string>? Notice;

    /// <summary>The user wants to clear the queue: the window asks first.</summary>
    public event Action? ClearRequested;

    /// <summary>The user wants to load (replace = true) or append a saved queue: the window asks before replacing.</summary>
    public event Action<SavedQueue, bool>? LoadRequested;

    /// <summary>Rebuilds the list when the queue (or the current track) changed since the last call.</summary>
    public void Refresh() => Rebuild(force: false);

    /// <summary>Handles the tab's keys. Returns true when the key was used.</summary>
    public bool HandleKey(Key key)
    {
        if (_showSaved)
        {
            return HandleSavedKey(key);
        }

        var entry = List.SelectedRow?.Data as QueueEntry;
        if (key == Key.Delete || Is(key, 'x'))
        {
            if (entry is not null)
            {
                _player.RemoveFromQueue(entry.Index);
            }

            return true;
        }

        if (key == Key.CursorUp.WithShift || key == Key.CursorDown.WithShift)
        {
            if (entry is not null)
            {
                var to = entry.Index + (key == Key.CursorUp.WithShift ? -1 : 1);
                if (to >= 0 && to < _player.Queue.Count)
                {
                    _player.MoveInQueue(entry.Index, to);
                    _pendingSelection = to;
                }
            }

            return true;
        }

        if (Is(key, 'c'))
        {
            if (_player.Queue.Count > 0)
            {
                ClearRequested?.Invoke();
            }

            return true;
        }

        if (Is(key, 's'))
        {
            BeginNaming();
            return true;
        }

        if (Is(key, 'o'))
        {
            _showSaved = true;
            Rebuild(force: true);
            return true;
        }

        return false;
    }

    /// <summary>Saves the current queue under the typed name and closes the name bar.</summary>
    public void ConfirmName()
    {
        var name = _nameBar.Field.Text.Trim();
        if (name.Length == 0)
        {
            return;
        }

        var tracks = _player.Queue
            .Where(e => !string.IsNullOrEmpty(e.Track.Url))
            .Select(e => new SavedTrack(e.Track.Url!, e.Track.Title, e.Track.Artist, e.Album.Title, e.Track.Duration.TotalSeconds))
            .ToList();
        CancelNaming();
        if (tracks.Count == 0)
        {
            Notice?.Invoke("Nothing to save: the queue is empty or the server sent no file addresses.");
            return;
        }

        try
        {
            _store.Save(name, tracks);
            Notice?.Invoke($"Saved \"{name}\" ({tracks.Count} tracks). Press o to see saved queues.");
        }
        catch (IOException ex)
        {
            Notice?.Invoke("Could not save the queue: " + ex.Message);
        }
    }

    /// <summary>Closes the name bar without saving.</summary>
    public void CancelNaming()
    {
        _nameBar.Visible = false;
        List.SetFocus();
        SetNeedsDraw();
    }

    private int? _pendingSelection;
    private bool _positioned;

    private static bool Is(Key key, char c) => !key.IsCtrl && !key.IsAlt && key.AsRune.Value == c;

    private bool HandleSavedKey(Key key)
    {
        var saved = List.SelectedRow?.Data as SavedQueue;
        if (key == Key.Esc || Is(key, 'o'))
        {
            _showSaved = false;
            Rebuild(force: true);
            return true;
        }

        if (key == Key.Delete || Is(key, 'x'))
        {
            if (saved is not null)
            {
                _store.Delete(saved.Name);
                Rebuild(force: true);
            }

            return true;
        }

        if (Is(key, 'a'))
        {
            if (saved is not null)
            {
                LoadRequested?.Invoke(saved, false);
            }

            return true;
        }

        return false;
    }

    private void BeginNaming()
    {
        if (_player.Queue.Count == 0)
        {
            Notice?.Invoke("The queue is empty, nothing to save.");
            return;
        }

        _nameBar.Field.Text = $"Queue {DateTime.Now:yyyy-MM-dd HH:mm}";
        _nameBar.Visible = true;
        _nameBar.Field.SetFocus();
        SetNeedsDraw();
    }

    private void OnActivated(Row row)
    {
        switch (row.Data)
        {
            case QueueEntry entry:
                _player.JumpTo(entry.Index);
                break;
            case SavedQueue saved:
                LoadRequested?.Invoke(saved, true);
                break;
        }
    }

    private void Rebuild(bool force)
    {
        var queue = _player.Queue;
        var signature = _showSaved
            ? "saved"
            : $"{queue.Count}:{_player.QueueIndex}:{string.Join(',', queue.Take(400).Select(e => e.Track.Id))}";
        if (!force && signature == _signature && _pendingSelection is null)
        {
            return;
        }

        _signature = signature;
        var keep = List.SelectedIndex;
        var rows = _showSaved ? SavedRows() : QueueRows(queue);
        List.Rows = rows;

        var target = _pendingSelection is { } p ? p + 1 : keep;     // +1: the heading row
        _pendingSelection = null;
        if (target >= 0 && target < rows.Count && rows[target].Kind != RowKind.Header)
        {
            List.SelectIndex(target);
        }

        // First look at a real queue: start on the track that is playing (once, so later refreshes keep the user's place).
        if (!_showSaved && _player.QueueIndex >= 0 && queue.Count > 0)
        {
            if (!_positioned && _player.QueueIndex + 1 < rows.Count)
            {
                List.SelectIndex(_player.QueueIndex + 1);
            }

            _positioned = true;
        }
    }

    private List<Row> QueueRows(IReadOnlyList<QueueEntry> queue)
    {
        var rows = new List<Row>();
        if (queue.Count == 0)
        {
            rows.Add(new Row("Queue", Kind: RowKind.Header));
            rows.Add(new Row("The queue is empty. Press Enter on an album or track to play it, or a / n to add it here.", Kind: RowKind.More));
            return rows;
        }

        var total = TimeSpan.FromTicks(queue.Sum(e => e.Track.Duration.Ticks));
        var left = TimeSpan.FromTicks(queue.Where(e => e.Index > _player.QueueIndex).Sum(e => e.Track.Duration.Ticks));
        rows.Add(new Row(
            $"Queue — {queue.Count} tracks, {Format(total)}   ·   {Format(left)} after the current track   ·   Enter play  x remove  Shift+↑↓ move  c clear  s save  o saved queues",
            Kind: RowKind.Header));
        foreach (var e in queue)
        {
            var marker = e.Index == _player.QueueIndex ? (_player.IsPlaying ? "▶" : "▮") : " ";
            var who = string.IsNullOrEmpty(e.Track.Artist) ? e.Album.ArtistName : e.Track.Artist;
            // "artist · album · title", the title in bold.
            var prefix = $"{marker} {e.Index + 1,3}. {who} · {e.Album.Title} · ";
            rows.Add(new Row(prefix + e.Track.Title, Format(e.Track.Duration), Data: e, Bold: (prefix.Length, e.Track.Title.Length)));
        }

        return rows;
    }

    private List<Row> SavedRows()
    {
        var saved = _store.List();
        var rows = new List<Row> { new($"Saved queues ({saved.Count})   ·   Enter replace the queue  a append  x delete  Esc back", Kind: RowKind.Header) };
        if (saved.Count == 0)
        {
            rows.Add(new Row("Nothing saved yet. Press Esc, then s to save the current queue.", Kind: RowKind.More));
        }

        rows.AddRange(saved.Select(q => new Row(q.Name, $"{q.Tracks.Count} tracks · {q.SavedAt:yyyy-MM-dd}", Data: q)));
        return rows;
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    /// <summary>"Save queue as:" and a text field, on the last row of the tab.</summary>
    private sealed class NameBar : View
    {
        public NameBar()
        {
            CanFocus = true;
            Field = new TextField { X = 18, Y = 0, Width = 40, Height = 1 };
            Add(new Caption { X = 1, Y = 0, Width = 17, Height = 1 }, Field);
        }

        public TextField Field { get; }

        private sealed class Caption : PaintView
        {
            protected override void Paint() => Put(0, 0, "Save queue as:", Theme.Heading);
        }
    }
}
