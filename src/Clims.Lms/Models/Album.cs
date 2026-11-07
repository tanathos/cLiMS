using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

/// <summary>An album as listed by <c>albums</c>.</summary>
/// <param name="Id">LMS album id.</param>
/// <param name="Title">Album title.</param>
/// <param name="ArtistName">Album artist, when requested with <see cref="AlbumTags.Artist"/>.</param>
/// <param name="ArtistId">Album artist id, when requested with <see cref="AlbumTags.ArtistId"/>.</param>
/// <param name="Year">Release year, 0 when unknown.</param>
/// <param name="ArtworkTrackId">Id for cover art URLs (see <see cref="ArtworkUrl"/>); null when the album has no artwork.</param>
/// <param name="TextKey">First-letter key for A-Z jumping.</param>
/// <param name="IsCompilation">True for compilations.</param>
/// <param name="ReleaseType">LMS release type such as ALBUM, SINGLE or EP.</param>
/// <param name="DiscCount">Number of discs when known.</param>
public sealed record Album(
    int Id,
    string Title,
    string? ArtistName,
    int? ArtistId,
    int Year,
    string? ArtworkTrackId,
    string? TextKey,
    bool IsCompilation,
    string? ReleaseType,
    int? DiscCount)
{
    internal static Album From(JsonElement e) =>
        new(
            e.GetInt("id"),
            e.GetStringOrNull("album") ?? e.GetStringOrNull("title") ?? string.Empty,
            e.GetStringOrNull("artist"),
            e.GetIntOrNull("artist_id"),
            e.GetInt("year"),
            e.GetStringOrNull("artwork_track_id"),
            e.GetStringOrNull("textkey"),
            e.GetBool("compilation"),
            e.GetStringOrNull("release_type"),
            e.GetIntOrNull("disccount"));
}
