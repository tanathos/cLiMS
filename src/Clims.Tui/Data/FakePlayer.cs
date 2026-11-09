using Clims.Catalog;
using Clims.Tui.Ui;

namespace Clims.Tui.Data;

/// <summary>A pretend player with a fake clock and a local queue, for the demo mode (no server).</summary>
public sealed class FakePlayer : IPlayerController
{
    private readonly List<(CatalogAlbum Album, CatalogTrack Track)> _items = new();
    private int _index;

    public FakePlayer(string playerName = "piCorePlayer")
    {
        PlayerName = playerName;
    }

    public string PlayerName { get; }

    public bool IsPlaying { get; private set; }

    public int Volume { get; private set; } = 70;

    public NowPlaying? Current => _items.Count > 0 ? new NowPlaying(_items[_index].Album, _items[_index].Track) : null;

    public TimeSpan Elapsed { get; private set; }

    public bool Connected => true;

    public string? Problem => null;

    public IReadOnlyList<QueueEntry> Queue => _items.Select((x, i) => new QueueEntry(i, x.Track, x.Album)).ToList();

    public int QueueIndex => _items.Count > 0 ? _index : -1;

    public event Action? Changed;

    public event Action<string>? Notice;

    public void TogglePlay()
    {
        if (Current is null)
        {
            return;
        }

        IsPlaying = !IsPlaying;
        Changed?.Invoke();
    }

    public void Next() => Go(_index + 1);

    public void Previous() => Go(Elapsed > TimeSpan.FromSeconds(3) ? _index : _index - 1);

    public void Seek(TimeSpan delta)
    {
        if (Current is not { } now)
        {
            return;
        }

        Elapsed = TimeSpan.FromSeconds(Math.Clamp((Elapsed + delta).TotalSeconds, 0, Math.Max(0, now.Track.Duration.TotalSeconds - 0.1)));
        Changed?.Invoke();
    }

    public void SeekTo(double fraction)
    {
        if (Current is not { } now)
        {
            return;
        }

        Elapsed = TimeSpan.FromSeconds(Math.Clamp(fraction, 0, 1) * now.Track.Duration.TotalSeconds);
        Changed?.Invoke();
    }

    public void SetVolume(int volume)
    {
        Volume = Math.Clamp(volume, 0, 100);
        Changed?.Invoke();
    }

    public void Play(CatalogAlbum album, IReadOnlyList<CatalogTrack> tracks, int index)
    {
        if (tracks.Count == 0)
        {
            return;
        }

        _items.Clear();
        _items.AddRange(tracks.Select(t => (album, t)));
        IsPlaying = true;
        Go(index);
    }

    public void Enqueue(QueueAction action, CatalogAlbum album, IReadOnlyList<CatalogTrack> tracks)
    {
        if (tracks.Count == 0)
        {
            return;
        }

        var at = action == QueueAction.Add || _items.Count == 0 ? _items.Count : _index + 1;
        _items.InsertRange(at, tracks.Select(t => (album, t)));
        Changed?.Invoke();
        Notice?.Invoke(action == QueueAction.Add ? "Added to the queue (demo)." : "Will play next (demo).");
    }

    public void PlayTracks(IReadOnlyList<(CatalogAlbum Album, CatalogTrack Track)> tracks, bool replace)
    {
        if (tracks.Count == 0)
        {
            return;
        }

        if (replace)
        {
            _items.Clear();
            _items.AddRange(tracks);
            IsPlaying = true;
            Go(0);
            return;
        }

        _items.AddRange(tracks);
        Changed?.Invoke();
        Notice?.Invoke("Added to the queue (demo).");
    }


    public void JumpTo(int index) => Go(index);

    public void RemoveFromQueue(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        _items.RemoveAt(index);
        if (index < _index)
        {
            _index--;
        }

        _index = Math.Clamp(_index, 0, Math.Max(0, _items.Count - 1));
        Changed?.Invoke();
    }

    public void MoveInQueue(int from, int to)
    {
        if (from < 0 || from >= _items.Count || to < 0 || to >= _items.Count || from == to)
        {
            return;
        }

        var playing = _items[_index];
        var item = _items[from];
        _items.RemoveAt(from);
        _items.Insert(to, item);
        _index = _items.IndexOf(playing);
        Changed?.Invoke();
    }

    public void ClearQueue()
    {
        _items.Clear();
        _index = 0;
        IsPlaying = false;
        Elapsed = TimeSpan.Zero;
        Changed?.Invoke();
    }

    public void PlaySaved(SavedQueue queue, bool replace) => Notice?.Invoke("Saved queues need a server (demo mode has none).");

    public void Tick(TimeSpan dt)
    {
        if (!IsPlaying || Current is not { } now)
        {
            return;
        }

        Elapsed += dt;
        if (Elapsed >= now.Track.Duration)
        {
            Go(_index + 1);
        }
    }

    private void Go(int index)
    {
        if (_items.Count == 0)
        {
            return;
        }

        _index = (index + _items.Count) % _items.Count;
        Elapsed = TimeSpan.Zero;
        Changed?.Invoke();
    }
}
