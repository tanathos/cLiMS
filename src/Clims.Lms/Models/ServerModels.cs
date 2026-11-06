using System.Globalization;
using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

/// <summary>A player known to the server (a Squeezelite instance, a web client, ...).</summary>
/// <param name="PlayerId">Player id, normally the MAC address; pass it to player commands.</param>
/// <param name="Name">Display name.</param>
/// <param name="Model">Model string such as <c>squeezelite</c> or <c>http</c>.</param>
/// <param name="Ip">IP address and port of the player.</param>
/// <param name="IsConnected">True while the player is connected to the server.</param>
/// <param name="IsPlaying">True while playing.</param>
/// <param name="IsPoweredOn">Power state.</param>
/// <param name="IsHardwarePlayer">False for web clients such as a browser tab.</param>
public sealed record PlayerInfo(
    string PlayerId,
    string Name,
    string? Model,
    string? Ip,
    bool IsConnected,
    bool IsPlaying,
    bool IsPoweredOn,
    bool IsHardwarePlayer)
{
    internal static PlayerInfo From(JsonElement e) =>
        new(
            e.GetStringOrNull("playerid") ?? string.Empty,
            e.GetStringOrNull("name") ?? string.Empty,
            e.GetStringOrNull("model"),
            e.GetStringOrNull("ip"),
            e.GetBool("connected"),
            e.GetBool("isplaying"),
            e.GetBool("power"),
            !e.TryGet("isplayer", out _) || e.GetBool("isplayer"));
}

/// <summary>Result of <c>serverstatus</c>: version, library totals, last scan time and players.</summary>
/// <param name="Version">Server version, for example 9.1.1.</param>
/// <param name="Uuid">Server UUID.</param>
/// <param name="LastScan">When the library was last scanned. Unchanged by a scan that found nothing new.</param>
/// <param name="AlbumCount">Total albums.</param>
/// <param name="ArtistCount">Total artists.</param>
/// <param name="GenreCount">Total genres.</param>
/// <param name="TrackCount">Total tracks.</param>
/// <param name="TotalDuration">Combined length of all tracks.</param>
/// <param name="Players">Known players.</param>
public sealed record ServerStatus(
    string Version,
    string? Uuid,
    DateTimeOffset? LastScan,
    int AlbumCount,
    int ArtistCount,
    int GenreCount,
    int TrackCount,
    TimeSpan TotalDuration,
    IReadOnlyList<PlayerInfo> Players)
{
    internal static ServerStatus From(JsonElement r)
    {
        DateTimeOffset? lastScan = r.GetDoubleOrNull("lastscan") is { } s && s > 0
            ? DateTimeOffset.FromUnixTimeSeconds((long)s)
            : null;
        return new ServerStatus(
            r.GetStringOrNull("version") ?? string.Empty,
            r.GetStringOrNull("uuid"),
            lastScan,
            r.GetInt("info total albums"),
            r.GetInt("info total artists"),
            r.GetInt("info total genres"),
            r.GetInt("info total songs"),
            r.GetSeconds("info total duration"),
            r.GetLoop("players_loop").Select(PlayerInfo.From).ToList());
    }
}

/// <summary>Library scan progress from <c>rescanprogress</c>.</summary>
/// <param name="IsScanning">True while a scan runs. A scan takes minutes and disconnects players at the end.</param>
/// <param name="Details">Raw progress fields (step names and percentages) while scanning.</param>
public sealed record ScanState(bool IsScanning, IReadOnlyDictionary<string, string> Details)
{
    internal static ScanState From(JsonElement r)
    {
        var details = new Dictionary<string, string>();
        if (r.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in r.EnumerateObject())
            {
                if (p.Name != "rescan" && r.GetStringOrNull(p.Name) is { } text)
                {
                    details[p.Name] = text;
                }
            }
        }

        return new ScanState(r.GetBool("rescan"), details);
    }
}

/// <summary>Play state of a player.</summary>
public enum PlayMode
{
    /// <summary>Stopped.</summary>
    Stop,

    /// <summary>Playing.</summary>
    Play,

    /// <summary>Paused.</summary>
    Pause,
}

/// <summary>Repeat setting.</summary>
public enum RepeatMode
{
    /// <summary>No repeat.</summary>
    Off = 0,

    /// <summary>Repeat the current track.</summary>
    Track = 1,

    /// <summary>Repeat the whole queue.</summary>
    Queue = 2,
}

/// <summary>Shuffle setting.</summary>
public enum ShuffleMode
{
    /// <summary>In order.</summary>
    Off = 0,

    /// <summary>Shuffle tracks.</summary>
    Tracks = 1,

    /// <summary>Shuffle albums.</summary>
    Albums = 2,
}

/// <summary>A track in a player's queue.</summary>
/// <param name="Index">Zero-based position in the queue.</param>
/// <param name="Track">The track.</param>
public sealed record QueueItem(int Index, Track Track);

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

internal static class Invariant
{
    public static string Seconds(TimeSpan t) => t.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}
