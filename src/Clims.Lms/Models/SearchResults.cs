using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

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
