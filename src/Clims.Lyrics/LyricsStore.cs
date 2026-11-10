using Microsoft.Data.Sqlite;

namespace Clims.Lyrics;

/// <summary>
/// The lyrics found so far, kept for good in a SQLite file of their own (one for all servers: lyrics belong to the song,
/// not to a library). A found lyric is never evicted or expired. A search that found nothing is remembered too (a "miss"),
/// but only for a while, because a provider may get the song later. Every failure (locked, corrupt or read-only file) reads
/// as "nothing stored" and writes are dropped: the caller then simply searches again.
/// </summary>
public sealed class LyricsStore
{
    private readonly string _path;
    private readonly object _gate = new();

    /// <summary>Creates the store for a file; the file and its folder are created on the first write.</summary>
    public LyricsStore(string path)
    {
        _path = path;
    }

    /// <summary>Path of the database file.</summary>
    public string Path => _path;

    /// <summary>The stored lyrics of a key, or null.</summary>
    public LyricsResult? Get(string key)
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
                using var cmd = Command(connection, "SELECT plain, synced, source, instrumental FROM lyrics WHERE key = $k");
                cmd.Parameters.AddWithValue("$k", key);
                using var r = cmd.ExecuteReader();
                return r.Read() ? new LyricsResult(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetInt32(3) != 0) : null;
            }
            catch (Exception ex) when (IsStorageError(ex))
            {
                return null;
            }
        }
    }

    /// <summary>True when the stored lyrics of a key were put there by hand (they are never replaced by a search).</summary>
    public bool IsUserEdited(string key)
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return false;
            }

            try
            {
                using var connection = Open();
                using var cmd = Command(connection, "SELECT user_edited FROM lyrics WHERE key = $k");
                cmd.Parameters.AddWithValue("$k", key);
                return cmd.ExecuteScalar() is long edited && edited != 0;
            }
            catch (Exception ex) when (IsStorageError(ex))
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Stores lyrics (and forgets any miss for the key). With <paramref name="replace"/> false an existing entry is kept,
    /// and an entry marked as hand-made is never replaced.
    /// </summary>
    public void Put(LyricsQuery query, LyricsResult result, DateTimeOffset now, bool userEdited = false, bool replace = true)
    {
        var key = query.Key;
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path))!);
                using var connection = Open();
                EnsureTables(connection);
                var verb = replace ? "INSERT OR REPLACE" : "INSERT OR IGNORE";
                using (var cmd = Command(
                    connection,
                    verb + " INTO lyrics(key, artist, album, title, duration_ms, plain, synced, source, instrumental, fetched, user_edited) " +
                    "SELECT $k, $ar, $al, $t, $d, $p, $s, $src, $i, $f, $u WHERE NOT EXISTS (SELECT 1 FROM lyrics WHERE key = $k AND user_edited = 1 AND $u = 0)"))
                {
                    cmd.Parameters.AddWithValue("$k", key);
                    cmd.Parameters.AddWithValue("$ar", query.Artist);
                    cmd.Parameters.AddWithValue("$al", query.Album);
                    cmd.Parameters.AddWithValue("$t", query.Title);
                    cmd.Parameters.AddWithValue("$d", (long)query.Duration.TotalMilliseconds);
                    cmd.Parameters.AddWithValue("$p", result.Plain);
                    cmd.Parameters.AddWithValue("$s", (object?)result.SyncedLrc ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$src", result.Source);
                    cmd.Parameters.AddWithValue("$i", result.Instrumental ? 1 : 0);
                    cmd.Parameters.AddWithValue("$f", now.ToUnixTimeSeconds());
                    cmd.Parameters.AddWithValue("$u", userEdited ? 1 : 0);
                    cmd.ExecuteNonQuery();
                }

                using var clear = Command(connection, "DELETE FROM misses WHERE key = $k");
                clear.Parameters.AddWithValue("$k", key);
                clear.ExecuteNonQuery();
            }
            catch (Exception ex) when (IsStorageError(ex))
            {
                // Lyrics that cannot be saved are just searched for again next time.
            }
        }
    }

    /// <summary>Removes the lyrics (and the miss) of a key.</summary>
    public void Delete(string key)
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return;
            }

            try
            {
                using var connection = Open();
                EnsureTables(connection);
                foreach (var table in new[] { "lyrics", "misses" })
                {
                    using var cmd = Command(connection, $"DELETE FROM {table} WHERE key = $k");
                    cmd.Parameters.AddWithValue("$k", key);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex) when (IsStorageError(ex))
            {
                // Nothing to do: the entry stays.
            }
        }
    }

    /// <summary>Remembers that the providers named in <paramref name="providers"/> found nothing for the key.</summary>
    public void PutMiss(string key, DateTimeOffset now, IEnumerable<string> providers)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path))!);
                using var connection = Open();
                EnsureTables(connection);
                using var cmd = Command(connection, "INSERT OR REPLACE INTO misses(key, checked, providers) VALUES ($k, $c, $p)");
                cmd.Parameters.AddWithValue("$k", key);
                cmd.Parameters.AddWithValue("$c", now.ToUnixTimeSeconds());
                cmd.Parameters.AddWithValue("$p", string.Join(',', providers.OrderBy(p => p, StringComparer.Ordinal)));
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) when (IsStorageError(ex))
            {
                // A miss that is not remembered costs one more search.
            }
        }
    }

    /// <summary>
    /// True when a search for the key found nothing less than <paramref name="retry"/> ago and every provider in
    /// <paramref name="providers"/> was part of it (a provider enabled since then deserves its try at once).
    /// </summary>
    public bool IsRecentMiss(string key, DateTimeOffset now, TimeSpan retry, IEnumerable<string> providers)
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return false;
            }

            try
            {
                using var connection = Open();
                using var cmd = Command(connection, "SELECT checked, providers FROM misses WHERE key = $k");
                cmd.Parameters.AddWithValue("$k", key);
                using var r = cmd.ExecuteReader();
                if (!r.Read())
                {
                    return false;
                }

                var tried = r.GetString(1).Split(',', StringSplitOptions.RemoveEmptyEntries);
                return r.GetInt64(0) + (long)retry.TotalSeconds > now.ToUnixTimeSeconds() && providers.All(p => tried.Contains(p, StringComparer.Ordinal));
            }
            catch (Exception ex) when (IsStorageError(ex))
            {
                return false;
            }
        }
    }

    /// <summary>How many lyrics are stored (0 when the file does not exist or cannot be read).</summary>
    public int Count()
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
                using var cmd = Command(connection, "SELECT COUNT(*) FROM lyrics");
                return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (IsStorageError(ex))
            {
                return 0;
            }
        }
    }

    private static bool IsStorageError(Exception ex) => ex is SqliteException or IOException or UnauthorizedAccessException or InvalidCastException;

    private static void EnsureTables(SqliteConnection connection)
    {
        Exec(
            connection,
            "CREATE TABLE IF NOT EXISTS lyrics(key TEXT PRIMARY KEY, artist TEXT NOT NULL, album TEXT NOT NULL, title TEXT NOT NULL, duration_ms INTEGER NOT NULL, " +
            "plain TEXT NOT NULL, synced TEXT NULL, source TEXT NOT NULL, instrumental INTEGER NOT NULL, fetched INTEGER NOT NULL, user_edited INTEGER NOT NULL)");
        Exec(connection, "CREATE TABLE IF NOT EXISTS misses(key TEXT PRIMARY KEY, checked INTEGER NOT NULL, providers TEXT NOT NULL)");
    }

    private SqliteConnection Open()
    {
        // Pooling off so the file is released as soon as the connection closes (it must stay movable and deletable).
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
