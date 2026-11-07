using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

/// <summary>A track as listed by <c>titles</c>, <c>status</c> or <c>songinfo</c>. Fields not requested via tags are null.</summary>
/// <param name="Id">LMS track id; may change after a full rescan, so persist <see cref="Url"/> instead.</param>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Track artist.</param>
/// <param name="ArtistId">Artist id.</param>
/// <param name="Album">Album title.</param>
/// <param name="AlbumId">Album id.</param>
/// <param name="TrackNumber">Track number within its disc.</param>
/// <param name="DiscNumber">Disc number.</param>
/// <param name="Duration">Length of the track.</param>
/// <param name="Year">Year, when known.</param>
/// <param name="Url">File URL, the durable identifier of a track across rescans.</param>
/// <param name="Genre">Genre name.</param>
/// <param name="GenreId">Genre id.</param>
/// <param name="ArtworkTrackId">Id to build cover art URLs from.</param>
/// <param name="CoverId">LMS cover id (per track).</param>
/// <param name="Bitrate">Bitrate text as LMS formats it, for example <c>820kbps VBR</c>.</param>
/// <param name="SampleRate">Sample rate in Hz.</param>
/// <param name="SampleSize">Bits per sample.</param>
/// <param name="IsLossless">True for lossless formats such as FLAC.</param>
/// <param name="IsCompilation">True when the track belongs to a compilation.</param>
public sealed record Track(
    int Id,
    string Title,
    string? Artist,
    int? ArtistId,
    string? Album,
    int? AlbumId,
    int? TrackNumber,
    int? DiscNumber,
    TimeSpan Duration,
    int? Year,
    string? Url,
    string? Genre,
    int? GenreId,
    string? ArtworkTrackId,
    string? CoverId,
    string? Bitrate,
    int? SampleRate,
    int? SampleSize,
    bool? IsLossless,
    bool? IsCompilation)
{
    internal static Track From(JsonElement e) =>
        new(
            e.GetInt("id"),
            e.GetStringOrNull("title") ?? string.Empty,
            e.GetStringOrNull("artist"),
            e.GetIntOrNull("artist_id"),
            e.GetStringOrNull("album"),
            e.GetIntOrNull("album_id"),
            e.GetIntOrNull("tracknum"),
            e.GetIntOrNull("disc"),
            e.GetSeconds("duration"),
            e.GetIntOrNull("year"),
            e.GetStringOrNull("url"),
            e.GetStringOrNull("genre"),
            e.GetIntOrNull("genre_id"),
            e.GetStringOrNull("artwork_track_id"),
            e.GetStringOrNull("coverid"),
            e.GetStringOrNull("bitrate"),
            e.GetIntOrNull("samplerate"),
            e.GetIntOrNull("samplesize"),
            e.TryGet("lossless", out _) ? e.GetBool("lossless") : null,
            e.TryGet("compilation", out _) ? e.GetBool("compilation") : null);

    /// <summary>
    /// Builds a track from <c>songinfo</c>, which returns one single-property object per field in
    /// <c>songinfo_loop</c> instead of one object per track.
    /// </summary>
    internal static Track FromSongInfo(JsonElement result)
    {
        var merged = new Dictionary<string, JsonElement>();
        foreach (var part in result.GetLoop("songinfo_loop"))
        {
            foreach (var property in part.EnumerateObject())
            {
                merged[property.Name] = property.Value;
            }
        }

        // Re-serialise the merged fields as one object so the normal mapping applies.
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in merged)
            {
                writer.WritePropertyName(name);
                value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        return From(doc.RootElement);
    }
}
