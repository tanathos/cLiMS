using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

/// <summary>One page of a larger result set. <see cref="Total"/> is the server's full count, not the page size.</summary>
/// <typeparam name="T">Item type.</typeparam>
/// <param name="Items">Items in this page (empty when the server returned no loop).</param>
/// <param name="Total">Total number of matching items on the server.</param>
/// <param name="Start">Zero-based index of the first item.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int Start)
{
    /// <summary>True when more items exist after this page.</summary>
    public bool HasMore => Start + Items.Count < Total;
}

/// <summary>An artist. <see cref="TextKey"/> is the A-Z jump key LMS computes (first letter, digits and symbols grouped).</summary>
/// <param name="Id">LMS artist id; may change after a full rescan.</param>
/// <param name="Name">Display name.</param>
/// <param name="TextKey">First-letter key, when the server sent it.</param>
public sealed record Artist(int Id, string Name, string? TextKey)
{
    internal static Artist From(JsonElement e) =>
        new(e.GetInt("id"), e.GetStringOrNull("artist") ?? e.GetStringOrNull("title") ?? string.Empty, e.GetStringOrNull("textkey"));
}

/// <summary>A genre.</summary>
/// <param name="Id">LMS genre id.</param>
/// <param name="Name">Genre name.</param>
/// <param name="TextKey">First-letter key, when sent.</param>
public sealed record Genre(int Id, string Name, string? TextKey)
{
    internal static Genre From(JsonElement e) =>
        new(e.GetInt("id"), e.GetStringOrNull("genre") ?? e.GetStringOrNull("title") ?? string.Empty, e.GetStringOrNull("textkey"));
}

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

/// <summary>A server-side saved playlist.</summary>
/// <param name="Id">Playlist id.</param>
/// <param name="Name">Playlist name.</param>
public sealed record SavedPlaylist(int Id, string Name)
{
    internal static SavedPlaylist From(JsonElement e) =>
        new(e.GetInt("id"), e.GetStringOrNull("playlist") ?? e.GetStringOrNull("title") ?? string.Empty);
}

/// <summary>Result of the server's global <c>search</c> (word-prefix matches, accents folded).</summary>
/// <param name="Artists">Matching artists (id, name).</param>
/// <param name="Albums">Matching albums.</param>
/// <param name="Tracks">Matching tracks.</param>
/// <param name="Genres">Matching genres.</param>
/// <param name="ArtistCount">Total matching artists on the server.</param>
/// <param name="AlbumCount">Total matching albums on the server.</param>
/// <param name="TrackCount">Total matching tracks on the server.</param>
/// <param name="GenreCount">Total matching genres on the server.</param>
public sealed record SearchResults(
    IReadOnlyList<SearchHit> Artists,
    IReadOnlyList<SearchHit> Albums,
    IReadOnlyList<SearchHit> Tracks,
    IReadOnlyList<SearchHit> Genres,
    int ArtistCount,
    int AlbumCount,
    int TrackCount,
    int GenreCount)
{
    internal static SearchResults From(JsonElement r)
    {
        static IReadOnlyList<SearchHit> Hits(JsonElement r, string loop, string idKey, string nameKey) =>
            r.GetLoop(loop).Select(e => new SearchHit(e.GetInt(idKey), e.GetStringOrNull(nameKey) ?? string.Empty)).ToList();

        return new SearchResults(
            Hits(r, "contributors_loop", "contributor_id", "contributor"),
            Hits(r, "albums_loop", "album_id", "album"),
            Hits(r, "tracks_loop", "track_id", "track"),
            Hits(r, "genres_loop", "genre_id", "genre"),
            r.GetInt("contributors_count"),
            r.GetInt("albums_count"),
            r.GetInt("tracks_count"),
            r.GetInt("genres_count"));
    }
}

/// <summary>One search result: just an id and a display name.</summary>
/// <param name="Id">Artist, album, track or genre id.</param>
/// <param name="Name">Display name.</param>
public sealed record SearchHit(int Id, string Name);
