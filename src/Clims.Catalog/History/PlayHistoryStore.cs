using Microsoft.Data.Sqlite;

namespace Clims.Catalog;

/// <summary>
/// The play log, in a file of its own per server. Unlike the index and the cover cache it is never rebuilt or trimmed: a sync does not
/// touch it, and it is the only copy of what was played. Every failure is swallowed (a log that cannot be written must not stop the music).
/// </summary>
public sealed class PlayHistoryStore
{
    private readonly string _path;
    private readonly object _gate = new();

    /// <summary>Creates the store for a file; the file and its folder are created on the first <see cref="Add"/>.</summary>
    public PlayHistoryStore(string path)
    {
        _path = path;
    }

    /// <summary>Path of the database file.</summary>
    public string Path => _path;

    /// <summary>Appends one play.</summary>
    public void Add(PlayRecord play)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path))!);
                using var connection = Open();
                Exec(connection, "CREATE TABLE IF NOT EXISTS plays(id INTEGER PRIMARY KEY, played_at INTEGER NOT NULL, track_id INTEGER NOT NULL, album_id INTEGER NOT NULL, artist TEXT NOT NULL, album TEXT NOT NULL, title TEXT NOT NULL, tracknum INTEGER NOT NULL, album_tracks INTEGER NOT NULL)");
                Exec(connection, "CREATE INDEX IF NOT EXISTS plays_time ON plays(played_at)");
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO plays(played_at, track_id, album_id, artist, album, title, tracknum, album_tracks) VALUES ($t, $tr, $al, $ar, $ab, $ti, $n, $c)";
                cmd.Parameters.AddWithValue("$t", play.PlayedAt.ToUnixTimeSeconds());
                cmd.Parameters.AddWithValue("$tr", play.TrackId);
                cmd.Parameters.AddWithValue("$al", play.AlbumId);
                cmd.Parameters.AddWithValue("$ar", play.Artist);
                cmd.Parameters.AddWithValue("$ab", play.Album);
                cmd.Parameters.AddWithValue("$ti", play.Title);
                cmd.Parameters.AddWithValue("$n", play.TrackNumber);
                cmd.Parameters.AddWithValue("$c", play.AlbumTracks);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                // See the class summary.
            }
        }
    }

    /// <summary>Every play ever logged, oldest first (empty when there is no file or it cannot be read).</summary>
    public IReadOnlyList<PlayRecord> All()
    {
        lock (_gate)
        {
            var list = new List<PlayRecord>();
            if (!File.Exists(_path))
            {
                return list;
            }

            try
            {
                using var connection = Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT played_at, track_id, album_id, artist, album, title, tracknum, album_tracks FROM plays ORDER BY played_at, id";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new PlayRecord(r.GetInt32(1), r.GetInt32(2), DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(0)), r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt32(6), r.GetInt32(7)));
                }
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                // Whatever was read is shown.
            }

            return list;
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
