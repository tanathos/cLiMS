using Microsoft.Data.Sqlite;

namespace Clims.Info;

/// <summary>
/// What the providers answered, kept in a SQLite file of its own (one for all servers: an artist is the same on every server) so that
/// the next look at the same artist costs no request. Entries are texts (a result as JSON) or bytes (a picture) and expire after a
/// time given by the reader. Every failure (locked, corrupt or read-only file) reads as "nothing stored" and writes are dropped.
/// </summary>
public sealed class InfoStore
{
    private readonly string _path;
    private readonly object _gate = new();

    /// <summary>Creates the store for a file; the file and its folder are created on the first write.</summary>
    /// <param name="path">The database file.</param>
    public InfoStore(string path)
    {
        _path = path;
    }

    /// <summary>Path of the database file.</summary>
    public string Path => _path;

    /// <summary>The stored text, or null when missing, older than <paramref name="ttl"/> or unreadable.</summary>
    /// <param name="key">Entry key.</param>
    /// <param name="ttl">How old an entry may be.</param>
    /// <param name="now">The present moment.</param>
    public string? GetText(string key, TimeSpan ttl, DateTimeOffset now) => Read(key, ttl, now, "text") as string;

    /// <summary>The stored bytes, or null when missing, older than <paramref name="ttl"/> or unreadable.</summary>
    /// <param name="key">Entry key.</param>
    /// <param name="ttl">How old an entry may be.</param>
    /// <param name="now">The present moment.</param>
    public byte[]? GetBytes(string key, TimeSpan ttl, DateTimeOffset now) => Read(key, ttl, now, "data") as byte[];

    /// <summary>Stores a text under a key, replacing an older one.</summary>
    /// <param name="key">Entry key.</param>
    /// <param name="text">The text.</param>
    /// <param name="now">The present moment.</param>
    public void PutText(string key, string text, DateTimeOffset now) => Write(key, text, null, now);

    /// <summary>Stores bytes under a key, replacing an older entry.</summary>
    /// <param name="key">Entry key.</param>
    /// <param name="data">The bytes.</param>
    /// <param name="now">The present moment.</param>
    public void PutBytes(string key, byte[] data, DateTimeOffset now) => Write(key, null, data, now);

    private object? Read(string key, TimeSpan ttl, DateTimeOffset now, string column)
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
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT fetched, {column} FROM entries WHERE key = $k";
                cmd.Parameters.AddWithValue("$k", key);
                using var r = cmd.ExecuteReader();
                if (!r.Read() || r.IsDBNull(1) || r.GetInt64(0) + (long)ttl.TotalSeconds <= now.ToUnixTimeSeconds())
                {
                    return null;
                }

                return r.GetValue(1);
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    private void Write(string key, string? text, byte[]? data, DateTimeOffset now)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path))!);
                using var connection = Open();
                using (var create = connection.CreateCommand())
                {
                    create.CommandText = "CREATE TABLE IF NOT EXISTS entries(key TEXT PRIMARY KEY, fetched INTEGER NOT NULL, text TEXT, data BLOB)";
                    create.ExecuteNonQuery();
                }

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT OR REPLACE INTO entries(key, fetched, text, data) VALUES ($k, $f, $t, $d)";
                cmd.Parameters.AddWithValue("$k", key);
                cmd.Parameters.AddWithValue("$f", now.ToUnixTimeSeconds());
                cmd.Parameters.AddWithValue("$t", (object?)text ?? DBNull.Value);
                cmd.Parameters.Add("$d", SqliteType.Blob).Value = (object?)data ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                // A cache that cannot be written is just a cache that misses.
            }
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
}
