using System.Globalization;
using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

/// <summary>
/// Snapshot of a player: transport state, volume and a window of its queue. LMS only reports the elapsed time at the
/// moment of the request, so keep <see cref="ReceivedAt"/> and use <see cref="ElapsedAt"/> to interpolate between polls.
/// </summary>
/// <param name="Name">Player name.</param>
/// <param name="IsConnected">True while connected.</param>
/// <param name="IsPoweredOn">Power state.</param>
/// <param name="Mode">Stopped, playing or paused.</param>
/// <param name="Elapsed">Position in the current track when the reply arrived.</param>
/// <param name="Duration">Length of the current track, when known.</param>
/// <param name="Volume">Volume 0 to 100.</param>
/// <param name="Repeat">Repeat mode.</param>
/// <param name="Shuffle">Shuffle mode.</param>
/// <param name="CanSeek">Whether the current stream can be seeked.</param>
/// <param name="CurrentIndex">Queue index of the current track, null when the queue is empty.</param>
/// <param name="QueueLength">Number of tracks in the queue.</param>
/// <param name="QueueTimestamp">Changes whenever the queue changes; compare it to detect edits.</param>
/// <param name="Items">The requested window of the queue.</param>
/// <param name="ReceivedAt">When this snapshot arrived, for time interpolation.</param>
public sealed record PlayerStatus(
    string Name,
    bool IsConnected,
    bool IsPoweredOn,
    PlayMode Mode,
    TimeSpan Elapsed,
    TimeSpan? Duration,
    int Volume,
    RepeatMode Repeat,
    ShuffleMode Shuffle,
    bool CanSeek,
    int? CurrentIndex,
    int QueueLength,
    double? QueueTimestamp,
    IReadOnlyList<QueueItem> Items,
    DateTimeOffset ReceivedAt)
{
    /// <summary>Estimated position at <paramref name="now"/>: linear interpolation while playing, clamped to the duration.</summary>
    public TimeSpan ElapsedAt(DateTimeOffset now)
    {
        // A clock that went backwards must not move the playhead backwards.
        if (Mode != PlayMode.Play || now <= ReceivedAt)
        {
            return Elapsed;
        }

        var estimate = Elapsed + (now - ReceivedAt);
        return Duration is { } d && estimate > d ? d : estimate;
    }

    /// <summary>The current track if it is inside the returned queue window.</summary>
    public Track? CurrentTrack => CurrentIndex is { } i ? Items.FirstOrDefault(x => x.Index == i)?.Track : null;

    internal static PlayerStatus From(JsonElement r, DateTimeOffset receivedAt)
    {
        var items = r.GetLoop("playlist_loop")
            .Select(e => new QueueItem(e.GetInt("playlist index"), Track.From(e)))
            .ToList();
        var mode = r.GetStringOrNull("mode") switch
        {
            "play" => PlayMode.Play,
            "pause" => PlayMode.Pause,
            _ => PlayMode.Stop,
        };
        var queueLength = r.GetInt("playlist_tracks");

        return new PlayerStatus(
            r.GetStringOrNull("player_name") ?? string.Empty,
            r.GetBool("player_connected"),
            r.GetBool("power"),
            mode,
            r.GetSeconds("time"),
            r.TryGet("duration", out _) ? r.GetSeconds("duration") : null,
            r.GetInt("mixer volume"),
            (RepeatMode)Math.Clamp(r.GetInt("playlist repeat"), 0, 2),
            (ShuffleMode)Math.Clamp(r.GetInt("playlist shuffle"), 0, 2),
            r.GetBool("can_seek"),
            queueLength > 0 ? r.GetIntOrNull("playlist_cur_index") : null,
            queueLength,
            r.GetDoubleOrNull("playlist_timestamp"),
            items,
            receivedAt);
    }
}
