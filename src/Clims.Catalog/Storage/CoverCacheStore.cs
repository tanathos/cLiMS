using Microsoft.Data.Sqlite;

namespace Clims.Catalog;

/// <summary>
/// Cover images (the server's JPEG/PNG bytes) kept in their own SQLite file, apart from the catalog index, so that
/// re-syncing or rebuilding the index never touches them and the file can be deleted on its own. Entries are keyed by
/// artwork id and pixel size. Every failure (locked, corrupt or read-only file) is a cache miss, never an error:
/// the caller just fetches from the server.
/// </summary>
public sealed class CoverCacheStore
{
    private readonly string _path;
    private readonly object _gate = new();

    /// <summary>Creates the store for a file; the file and its folder are created on the first <see cref="Put"/>.</summary>
    public CoverCacheStore(string path)
    {
        _path = path;
    }

    /// <summary>Path of the database file.</summary>
    public string Path => _path;

    /// <summary>The stored bytes, or null when missing, older than <paramref name="ttl"/> (the entry is then removed) or unreadable.</summary>
    public byte[]? Get(string artworkId, int size, TimeSpan ttl, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            try
            {
                using var connection = Open();
                long fetched;
                byte[] data;
                using (var cmd = Command(connection, "SELECT fetched, data FROM covers WHERE artwork = $a AND size = $s"))
                {
                    cmd.Parameters.AddWithValue("$a", artworkId);
                    cmd.Parameters.AddWithValue("$s", size);
                    using var r = cmd.ExecuteReader();
                    if (!r.Read())
                    {
                        return null;
                    }

                    fetched = r.GetInt64(0);
                    data = (byte[])r.GetValue(1);
                }

                if (fetched + (long)ttl.TotalSeconds > now.ToUnixTimeSeconds())
                {
                    return data;
                }

                using var delete = Command(connection, "DELETE FROM covers WHERE artwork = $a AND size = $s");
                delete.Parameters.AddWithValue("$a", artworkId);
                delete.Parameters.AddWithValue("$s", size);
                delete.ExecuteNonQuery();
                return null;
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidCastException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Stores (or replaces) a cover, then trims that size: entries older than <paramref name="ttl"/> go, and when
    /// <paramref name="maxEntries"/> is above zero only that many of the newest are kept.
    /// </summary>
    public void Put(string artworkId, int size, byte[] data, DateTimeOffset now, TimeSpan ttl, int maxEntries = 0)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path))!);
                using var connection = Open();
                Exec(connection, "CREATE TABLE IF NOT EXISTS covers(artwork TEXT NOT NULL, size INTEGER NOT NULL, fetched INTEGER NOT NULL, data BLOB NOT NULL, PRIMARY KEY(artwork, size))");

                using (var cmd = Command(connection, "INSERT OR REPLACE INTO covers(artwork, size, fetched, data) VALUES ($a, $s, $f, $d)"))
                {
                    cmd.Parameters.AddWithValue("$a", artworkId);
                    cmd.Parameters.AddWithValue("$s", size);
                    cmd.Parameters.AddWithValue("$f", now.ToUnixTimeSeconds());
                    cmd.Parameters.Add("$d", SqliteType.Blob).Value = data;
                    cmd.ExecuteNonQuery();
                }

                using (var expire = Command(connection, "DELETE FROM covers WHERE size = $s AND fetched <= $cutoff"))
                {
                    expire.Parameters.AddWithValue("$s", size);
                    expire.Parameters.AddWithValue("$cutoff", now.ToUnixTimeSeconds() - (long)ttl.TotalSeconds);
                    expire.ExecuteNonQuery();
                }

                if (maxEntries > 0)
                {
                    using var cap = Command(
                        connection,
                        "DELETE FROM covers WHERE size = $s AND rowid NOT IN (SELECT rowid FROM covers WHERE size = $s ORDER BY fetched DESC, rowid DESC LIMIT $max)");
                    cap.Parameters.AddWithValue("$s", size);
                    cap.Parameters.AddWithValue("$max", maxEntries);
                    cap.ExecuteNonQuery();
                }
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                // A cache that cannot be written is just a cache that misses.
            }
        }
    }

    /// <summary>How many covers of a pixel size are stored (0 when the file does not exist or cannot be read).</summary>
    public int Count(int size)
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return 0;
            }

            try
            {
                using var connection = Open();
                using var cmd = Command(connection, "SELECT COUNT(*) FROM covers WHERE size = $s");
                cmd.Parameters.AddWithValue("$s", size);
                return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                return 0;
            }
        }
    }

    private SqliteConnection Open()
    {
        // Pooling off so the file is released as soon as the connection closes (the cache must stay deletable).
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var cmd = Command(connection, sql);
        cmd.ExecuteNonQuery();
    }
}
