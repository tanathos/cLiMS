using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

internal sealed class LibraryApi : ILibraryApi
{
    private readonly ILmsRpcClient _rpc;

    public LibraryApi(ILmsRpcClient rpc)
    {
        _rpc = rpc;
    }

    public async Task<Page<Artist>> GetArtistsAsync(ArtistQuery? query, int start, int count, CancellationToken ct)
    {
        var command = new List<object> { "artists", start, count };
        if (query?.GenreId is { } genre)
        {
            command.Add($"genre_id:{genre}");
        }

        AddSearch(command, query?.Search);
        var r = await Read(command, ct).ConfigureAwait(false);
        return new Page<Artist>(r.GetLoop("artists_loop").Select(Artist.From).ToList(), r.GetInt("count"), start);
    }

    public async Task<Page<Album>> GetAlbumsAsync(AlbumQuery? query, int start, int count, CancellationToken ct)
    {
        query ??= new AlbumQuery();
        var command = new List<object> { "albums", start, count, query.Tags.ToTagArgument(), $"sort:{SortName(query.Sort)}" };
        if (query.ArtistId is { } artist)
        {
            command.Add($"artist_id:{artist}");
        }

        if (query.GenreId is { } genre)
        {
            command.Add($"genre_id:{genre}");
        }

        if (query.Year is { } year)
        {
            command.Add($"year:{year}");
        }

        if (query.Compilation is { } compilation)
        {
            command.Add($"compilation:{(compilation ? 1 : 0)}");
        }

        AddSearch(command, query.Search);
        var r = await Read(command, ct).ConfigureAwait(false);
        return new Page<Album>(r.GetLoop("albums_loop").Select(Album.From).ToList(), r.GetInt("count"), start);
    }

    public async Task<Page<Track>> GetTracksAsync(TrackQuery query, int start, int count, CancellationToken ct)
    {
        var command = new List<object> { "titles", start, count, query.Tags.ToTagArgument(), $"sort:{SortName(query.Sort)}" };
        if (query.AlbumId is { } album)
        {
            command.Add($"album_id:{album}");
        }

        if (query.ArtistId is { } artist)
        {
            command.Add($"artist_id:{artist}");
        }

        if (query.GenreId is { } genre)
        {
            command.Add($"genre_id:{genre}");
        }

        AddSearch(command, query.Search);
        var r = await Read(command, ct).ConfigureAwait(false);
        return new Page<Track>(r.GetLoop("titles_loop").Select(Track.From).ToList(), r.GetInt("count"), start);
    }

    public async Task<Page<Genre>> GetGenresAsync(int start, int count, CancellationToken ct)
    {
        var r = await Read(new List<object> { "genres", start, count }, ct).ConfigureAwait(false);
        return new Page<Genre>(r.GetLoop("genres_loop").Select(Genre.From).ToList(), r.GetInt("count"), start);
    }

    public async Task<SearchResults> SearchAsync(string term, int count, CancellationToken ct)
    {
        var r = await Read(new List<object> { "search", 0, count, $"term:{term}" }, ct).ConfigureAwait(false);
        return SearchResults.From(r);
    }

    public async Task<Track?> GetTrackAsync(int trackId, TrackTags tags, CancellationToken ct)
    {
        var r = await Read(new List<object> { "titles", 0, 1, tags.ToTagArgument(), $"track_id:{trackId}" }, ct).ConfigureAwait(false);
        return r.GetLoop("titles_loop").Select(Track.From).Cast<Track?>().FirstOrDefault();
    }

    public async Task<Track?> GetTrackByUrlAsync(string url, TrackTags tags, CancellationToken ct)
    {
        // songinfo takes a large count because it returns one entry per field, not per track.
        var r = await Read(new List<object> { "songinfo", 0, 100, tags.ToTagArgument(), $"url:{url}" }, ct).ConfigureAwait(false);
        var track = Track.FromSongInfo(r);
        return track.Id == 0 && track.Title.Length == 0 ? null : track;
    }

    public async Task<Page<SavedPlaylist>> GetPlaylistsAsync(int start, int count, CancellationToken ct)
    {
        // With none saved the reply is just {}, which GetLoop/GetInt treat as an empty result.
        var r = await Read(new List<object> { "playlists", start, count }, ct).ConfigureAwait(false);
        return new Page<SavedPlaylist>(r.GetLoop("playlists_loop").Select(SavedPlaylist.From).ToList(), r.GetInt("count"), start);
    }

    private static void AddSearch(List<object> command, string? search)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            command.Add($"search:{search}");
        }
    }

    private static string SortName(AlbumSort sort) => sort switch
    {
        AlbumSort.ArtistAlbum => "artistalbum",
        AlbumSort.YearAlbum => "yearalbum",
        AlbumSort.YearArtistAlbum => "yearartistalbum",
        AlbumSort.New => "new",
        AlbumSort.Random => "random",
        _ => "album",
    };

    private static string SortName(TrackSort sort) => sort switch
    {
        TrackSort.Title => "title",
        TrackSort.TrackNumber => "tracknum",
        TrackSort.New => "new",
        TrackSort.Random => "random",
        _ => "albumtrack",
    };

    private Task<JsonElement> Read(List<object> command, CancellationToken ct) =>
        _rpc.RequestAsync(null, command, RequestKind.Read, ct);
}
