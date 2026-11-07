using Clims.Catalog;
using Clims.Tui.Ui;

namespace Clims.Tui.Data;

/// <summary>What the footer, the Queue tab and the global keys drive: the real player (LMS) or a fake one for the demo.</summary>
public interface IPlayerController
{
    string PlayerName { get; }

    bool IsPlaying { get; }

    int Volume { get; }

    NowPlaying? Current { get; }

    /// <summary>Position in the current track, interpolated between server polls.</summary>
    TimeSpan Elapsed { get; }

    /// <summary>False while the server or the player cannot be reached.</summary>
    bool Connected { get; }

    /// <summary>Why nothing is playing and the controls do nothing (no player found, offline); null when all is well.</summary>
    string? Problem { get; }

    /// <summary>The queue, in play order.</summary>
    IReadOnlyList<QueueEntry> Queue { get; }

    /// <summary>Index of the current track in <see cref="Queue"/>, or -1.</summary>
    int QueueIndex { get; }

    /// <summary>Raised when state changes (any thread; the UI marshals to its own).</summary>
    event Action? Changed;

    /// <summary>Raised with a message for the user when a command failed.</summary>
    event Action<string>? Notice;

    void TogglePlay();

    void Next();

    void Previous();

    void Seek(TimeSpan delta);

    void SeekTo(double fraction);

    void SetVolume(int volume);

    /// <summary>Replaces the queue with <paramref name="tracks"/> and plays from <paramref name="index"/>. Not gapless (the stream restarts).</summary>
    void Play(CatalogAlbum album, IReadOnlyList<CatalogTrack> tracks, int index);

    /// <summary>Adds to the end of the queue or right after the current track. Playback continues without a gap.</summary>
    void Enqueue(QueueAction action, CatalogAlbum album, IReadOnlyList<CatalogTrack> tracks);

    /// <summary>Plays the queue entry at <paramref name="index"/>.</summary>
    void JumpTo(int index);

    /// <summary>Removes one queue entry.</summary>
    void RemoveFromQueue(int index);

    /// <summary>Moves a queue entry (the new index is the position it ends up at).</summary>
    void MoveInQueue(int from, int to);

    /// <summary>Empties the queue and stops.</summary>
    void ClearQueue();

    /// <summary>Plays or appends a saved queue, resolving its files by URL (ids change after a rescan).</summary>
    void PlaySaved(SavedQueue queue, bool replace);

    /// <summary>Advances the fake clock; real implementations ignore it.</summary>
    void Tick(TimeSpan dt);
}
