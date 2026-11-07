using System.Globalization;
using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

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
