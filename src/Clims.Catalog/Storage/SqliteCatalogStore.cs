using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Clims.Catalog;

/// <summary>SQLite implementation of <see cref="ICatalogStore"/>.</summary>
public sealed class SqliteCatalogStore : ICatalogStore
{
    /// <summary>Bumped when the table layout changes; an index with another version is ignored and rebuilt.</summary>
    public const int SchemaVersion = 1;

    private readonly string _path;

    /// <summary>Creates the store for a file; the file and its folder are created on first save.</summary>
    public SqliteCatalogStore(string path)
    {
        _path = path;
    }

    /// <summary>Path of the database file.</summary>
    public string Path => _path;

    /// <inheritdoc />
    public Catalog? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            using var connection = Open();
            var meta = ReadMeta(connection);
            if (meta is null || !meta.TryGetValue("schema_version", out var version) || version != SchemaVersion.ToString(CultureInfo.InvariantCulture))
            {
                return null;
            }

            var catalogMeta = new CatalogMeta(
                meta.GetValueOrDefault("server_uuid") ?? string.Empty,
                long.TryParse(meta.GetValueOrDefault("last_scan"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var scan) ? scan : null,
                Int(meta, "tracks"),
                Int(meta, "albums"),
                Int(meta, "artists"),
                Int(meta, "genres"),
                DateTimeOffset.TryParse(meta.GetValueOrDefault("synced_at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : DateTimeOffset.MinValue);

            var genres = new List<CatalogGenre>();
            using (var cmd = Command(connection, "SELECT id, name FROM genres ORDER BY ord"))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    genres.Add(new CatalogGenre(r.GetInt32(0), r.GetString(1)));
                }
            }

            var artists = new List<CatalogArtist>();
            using (var cmd = Command(connection, "SELECT id, name FROM artists ORDER BY ord"))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    artists.Add(new CatalogArtist(r.GetInt32(0), r.GetString(1)));
                }
            }

            var albums = new List<CatalogAlbum>();
            using (var cmd = Command(connection, "SELECT id, title, artist_id, artist, year, artwork, compilation, discs FROM albums ORDER BY ord"))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    albums.Add(new CatalogAlbum(
                        r.GetInt32(0),
                        r.GetString(1),
                        r.IsDBNull(2) ? null : r.GetInt32(2),
                        r.GetString(3),
                        r.GetInt32(4),
                        r.IsDBNull(5) ? null : r.GetString(5),
                        r.GetInt32(6) != 0,
                        r.IsDBNull(7) ? null : r.GetInt32(7)));
                }
            }

            var tracks = new List<CatalogTrack>();
            using (var cmd = Command(connection, "SELECT id, album_id, title, artist, artist_id, disc, number, duration_ms, genre, url FROM tracks"))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    tracks.Add(ReadTrack(r));
                }
            }

            return new Catalog(genres, artists, albums, tracks, catalogMeta);
        }
        catch (SqliteException)
        {
            // A damaged cache file is not an error: it is only a cache. The caller rebuilds it.
            return null;
        }
    }

    /// <inheritdoc />
    public void Save(Catalog catalog)
    {
        if (catalog.Meta is not { } meta)
        {
            throw new ArgumentException("Only a complete catalogue with metadata can be saved.", nameof(catalog));
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path))!);
        using var connection = Open();
        EnsureSchema(connection);
        using var tx = connection.BeginTransaction();

        foreach (var table in new[] { "tracks", "albums", "artists", "genres", "meta" })
        {
            Exec(connection, tx, $"DELETE FROM {table}");
        }

        using (var cmd = Command(connection, "INSERT INTO genres(id, name, ord) VALUES ($id, $name, $ord)", tx))
        {
            var id = cmd.Parameters.Add("$id", SqliteType.Integer);
            var name = cmd.Parameters.Add("$name", SqliteType.Text);
            var ord = cmd.Parameters.Add("$ord", SqliteType.Integer);
            var i = 0;
            foreach (var g in catalog.Genres)
            {
                id.Value = g.Id;
                name.Value = g.Name;
                ord.Value = i++;
                cmd.ExecuteNonQuery();
            }
        }

        using (var cmd = Command(connection, "INSERT INTO artists(id, name, ord) VALUES ($id, $name, $ord)", tx))
        {
            var id = cmd.Parameters.Add("$id", SqliteType.Integer);
            var name = cmd.Parameters.Add("$name", SqliteType.Text);
            var ord = cmd.Parameters.Add("$ord", SqliteType.Integer);
            var i = 0;
            foreach (var a in catalog.Artists)
            {
                id.Value = a.Id;
                name.Value = a.Name;
                ord.Value = i++;
                cmd.ExecuteNonQuery();
            }
        }

        using (var cmd = Command(
                   connection,
                   "INSERT INTO albums(id, title, artist_id, artist, year, artwork, compilation, discs, ord) VALUES ($id, $title, $artistId, $artist, $year, $artwork, $comp, $discs, $ord)",
                   tx))
        {
            var id = cmd.Parameters.Add("$id", SqliteType.Integer);
            var title = cmd.Parameters.Add("$title", SqliteType.Text);
            var artistId = cmd.Parameters.Add("$artistId", SqliteType.Integer);
            var artist = cmd.Parameters.Add("$artist", SqliteType.Text);
            var year = cmd.Parameters.Add("$year", SqliteType.Integer);
            var artwork = cmd.Parameters.Add("$artwork", SqliteType.Text);
            var comp = cmd.Parameters.Add("$comp", SqliteType.Integer);
            var discs = cmd.Parameters.Add("$discs", SqliteType.Integer);
            var ord = cmd.Parameters.Add("$ord", SqliteType.Integer);
            var i = 0;
            foreach (var a in catalog.Albums)
            {
                id.Value = a.Id;
                title.Value = a.Title;
                artistId.Value = a.ArtistId is { } x ? x : DBNull.Value;
                artist.Value = a.ArtistName;
                year.Value = a.Year;
                artwork.Value = (object?)a.ArtworkId ?? DBNull.Value;
                comp.Value = a.IsCompilation ? 1 : 0;
                discs.Value = a.DiscCount is { } d ? d : DBNull.Value;
                ord.Value = i++;
                cmd.ExecuteNonQuery();
            }
        }

        InsertTracks(connection, tx, catalog.SnapshotTracks());

        foreach (var (key, value) in new (string, string?)[]
                 {
                     ("schema_version", SchemaVersion.ToString(CultureInfo.InvariantCulture)),
                     ("server_uuid", meta.ServerUuid),
                     ("last_scan", meta.LastScan?.ToString(CultureInfo.InvariantCulture)),
                     ("tracks", meta.TrackCount.ToString(CultureInfo.InvariantCulture)),
                     ("albums", meta.AlbumCount.ToString(CultureInfo.InvariantCulture)),
                     ("artists", meta.ArtistCount.ToString(CultureInfo.InvariantCulture)),
                     ("genres", meta.GenreCount.ToString(CultureInfo.InvariantCulture)),
                     ("synced_at", meta.SyncedAt.ToString("O", CultureInfo.InvariantCulture)),
                 })
        {
            using var cmd = Command(connection, "INSERT INTO meta(key, value) VALUES ($k, $v)", tx);
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", (object?)value ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <inheritdoc />
    public void ReplaceAlbumTracks(int albumId, IReadOnlyList<CatalogTrack> tracks)
    {
        if (!File.Exists(_path))
        {
            return; // nothing persisted yet (first sync in progress); the final save will include these tracks
        }

        using var connection = Open();
        using var tx = connection.BeginTransaction();
        using (var del = Command(connection, "DELETE FROM tracks WHERE album_id = $a", tx))
        {
            del.Parameters.AddWithValue("$a", albumId);
            del.ExecuteNonQuery();
        }

        InsertTracks(connection, tx, tracks);
        tx.Commit();
    }

    private static void InsertTracks(SqliteConnection connection, SqliteTransaction tx, IEnumerable<CatalogTrack> tracks)
    {
        using var cmd = Command(
            connection,
            "INSERT OR REPLACE INTO tracks(id, album_id, title, artist, artist_id, disc, number, duration_ms, genre, url) VALUES ($id, $album, $title, $artist, $artistId, $disc, $number, $dur, $genre, $url)",
            tx);
        var id = cmd.Parameters.Add("$id", SqliteType.Integer);
        var album = cmd.Parameters.Add("$album", SqliteType.Integer);
        var title = cmd.Parameters.Add("$title", SqliteType.Text);
        var artist = cmd.Parameters.Add("$artist", SqliteType.Text);
        var artistId = cmd.Parameters.Add("$artistId", SqliteType.Integer);
        var disc = cmd.Parameters.Add("$disc", SqliteType.Integer);
        var number = cmd.Parameters.Add("$number", SqliteType.Integer);
        var dur = cmd.Parameters.Add("$dur", SqliteType.Integer);
        var genre = cmd.Parameters.Add("$genre", SqliteType.Text);
        var url = cmd.Parameters.Add("$url", SqliteType.Text);
        foreach (var t in tracks)
        {
            id.Value = t.Id;
            album.Value = t.AlbumId;
            title.Value = t.Title;
            artist.Value = (object?)t.Artist ?? DBNull.Value;
            artistId.Value = t.ArtistId is { } a ? a : DBNull.Value;
            disc.Value = t.Disc;
            number.Value = t.Number;
            dur.Value = (long)t.Duration.TotalMilliseconds;
            genre.Value = (object?)t.Genre ?? DBNull.Value;
            url.Value = (object?)t.Url ?? DBNull.Value;
            cmd.ExecuteNonQuery();
        }
    }

    private static CatalogTrack ReadTrack(SqliteDataReader r) => new(
        r.GetInt32(0),
        r.GetInt32(1),
        r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.IsDBNull(4) ? null : r.GetInt32(4),
        r.GetInt32(5),
        r.GetInt32(6),
        TimeSpan.FromMilliseconds(r.GetInt64(7)),
        r.IsDBNull(8) ? null : r.GetString(8),
        r.IsDBNull(9) ? null : r.GetString(9));

    private static Dictionary<string, string?>? ReadMeta(SqliteConnection connection)
    {
        try
        {
            var meta = new Dictionary<string, string?>();
            using var cmd = Command(connection, "SELECT key, value FROM meta");
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                meta[r.GetString(0)] = r.IsDBNull(1) ? null : r.GetString(1);
            }

            return meta;
        }
        catch (SqliteException)
        {
            return null; // no meta table: not one of ours, or a half-created file
        }
    }

    private static int Int(Dictionary<string, string?> meta, string key) =>
        int.TryParse(meta.GetValueOrDefault(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static void EnsureSchema(SqliteConnection connection)
    {
        Exec(connection, null, "CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT)");
        Exec(connection, null, "CREATE TABLE IF NOT EXISTS genres(id INTEGER PRIMARY KEY, name TEXT NOT NULL, ord INTEGER NOT NULL)");
        Exec(connection, null, "CREATE TABLE IF NOT EXISTS artists(id INTEGER PRIMARY KEY, name TEXT NOT NULL, ord INTEGER NOT NULL)");
        Exec(
            connection,
            null,
            "CREATE TABLE IF NOT EXISTS albums(id INTEGER PRIMARY KEY, title TEXT NOT NULL, artist_id INTEGER, artist TEXT NOT NULL, year INTEGER NOT NULL, artwork TEXT, compilation INTEGER NOT NULL, discs INTEGER, ord INTEGER NOT NULL)");
        Exec(
            connection,
            null,
            "CREATE TABLE IF NOT EXISTS tracks(id INTEGER PRIMARY KEY, album_id INTEGER NOT NULL, title TEXT NOT NULL, artist TEXT, artist_id INTEGER, disc INTEGER NOT NULL, number INTEGER NOT NULL, duration_ms INTEGER NOT NULL, genre TEXT, url TEXT)");
        Exec(connection, null, "CREATE INDEX IF NOT EXISTS ix_tracks_album ON tracks(album_id)");
    }

    private SqliteConnection Open()
    {
        // Pooling off so the file is released as soon as the connection closes (the cache must stay deletable).
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, SqliteTransaction? tx = null)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        return cmd;
    }

    private static void Exec(SqliteConnection connection, SqliteTransaction? tx, string sql)
    {
        using var cmd = Command(connection, sql, tx);
        cmd.ExecuteNonQuery();
    }
}
