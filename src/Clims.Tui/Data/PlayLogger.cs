using Clims.Catalog;

namespace Clims.Tui.Data;

/// <summary>
/// Writes the play log: it watches the player and, once a track has played for half of its length (at most 4 minutes, the rule LMS uses for
/// its own play count), appends it to the <see cref="PlayHistoryStore"/>. A track that is skipped early, a radio stream and a track
/// that is not in the library are not logged. Repeating the same track logs it again.
/// </summary>
public sealed class PlayLogger : IDisposable
{
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan Shortest = TimeSpan.FromSeconds(5);

    private readonly IPlayerController _player;
    private readonly Func<PlayHistoryStore?> _store;
    private readonly Func<MusicCatalog?> _catalog;
    private readonly Func<DateTimeOffset> _clock;
    private Timer? _timer;
    private int _trackId = -1;
    private bool _logged;
    private TimeSpan _last;
    private DateTimeOffset _loggedAt = DateTimeOffset.MinValue;
    private int _loggedTrack = -1;

    public PlayLogger(IPlayerController player, Func<PlayHistoryStore?> store, Func<MusicCatalog?> catalog, Func<DateTimeOffset>? clock = null)
    {
        _player = player;
        _store = store;
        _catalog = catalog;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Raised after a play was logged (any thread): the Played tab lists it.</summary>
    public event Action? Logged;

    /// <summary>Starts looking every two seconds.</summary>
    public void Start() => _timer = new Timer(_ => Step(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

    /// <summary>Looks at the player once: the timer calls it, and so do the tests.</summary>
    public void Step()
    {
        if (_player.Current is not { IsRemote: false } now)
        {
            _trackId = -1;
            return;
        }

        var elapsed = _player.Elapsed;
        if (now.Track.Id != _trackId)
        {
            _trackId = now.Track.Id;

            // The position can still be the old track's for a moment after the change (gapless): that is not a play of this one.
            // Rewinding below re-arms it, and it is logged when it really has played.
            _logged = elapsed >= Threshold(now.Track.Duration);
        }
        else if (elapsed + TimeSpan.FromSeconds(5) < _last)
        {
            _logged = false; // back at the start: the track is repeating
        }

        _last = elapsed;
        if (_logged || !_player.IsPlaying || elapsed < Threshold(now.Track.Duration))
        {
            return;
        }

        _logged = true;
        var at = _clock();

        // A repeat cannot honestly come sooner than half a track after the last log of it: that is a position glitch, not a play.
        if (now.Track.Id == _loggedTrack && at - _loggedAt < Threshold(now.Track.Duration))
        {
            return;
        }

        _loggedAt = at;
        _loggedTrack = now.Track.Id;
        if (_store() is not { } store)
        {
            return;
        }

        var album = now.Album;
        store.Add(new PlayRecord(
            now.Track.Id,
            album.Id,
            at,
            album.ArtistName,
            album.Title,
            now.Track.Title,
            now.Track.Number,
            _catalog()?.TracksOf(album.Id).Count ?? 0));
        Logged?.Invoke();
    }

    /// <summary>How long a track of this length must have played to count: half of it, but at least 5 seconds and at most 4 minutes.</summary>
    public static TimeSpan Threshold(TimeSpan duration) =>
        duration <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : TimeSpan.FromTicks(Math.Clamp(duration.Ticks / 2, Shortest.Ticks, Longest.Ticks));

    public void Dispose() => _timer?.Dispose();
}
