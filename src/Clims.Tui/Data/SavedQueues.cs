using System.Text.Json;

namespace Clims.Tui.Data;

/// <summary>A track of a saved queue. The file URL is the durable key: track ids can change after a rescan.</summary>
/// <param name="Url">File URL as LMS reports it.</param>
/// <param name="Title">Title, for listing a queue whose files are not loaded.</param>
/// <param name="Artist">Artist.</param>
/// <param name="Album">Album title.</param>
/// <param name="Seconds">Length in seconds.</param>
public sealed record SavedTrack(string Url, string Title, string? Artist, string? Album, double Seconds);

/// <summary>A named queue kept on this PC.</summary>
/// <param name="Name">Name, unique ignoring case.</param>
/// <param name="SavedAt">When it was saved.</param>
/// <param name="Tracks">The tracks in order.</param>
public sealed record SavedQueue(string Name, DateTimeOffset SavedAt, IReadOnlyList<SavedTrack> Tracks);

/// <summary>
/// Named queues in one JSON file (<c>queues.json</c> next to the settings). Writes go through a temporary file, so a crash
/// cannot leave half a file; a file that cannot be read is moved aside (<c>.bad</c>) and the list starts empty.
/// </summary>
public sealed class SavedQueueStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly object _gate = new();

    /// <summary>Creates the store; nothing is read until used.</summary>
    public SavedQueueStore(string path)
    {
        _path = path;
    }

    /// <summary>Default location: <c>%LOCALAPPDATA%\cLiMS\queues.json</c>.</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cLiMS", "queues.json");

    /// <summary>All saved queues, newest first.</summary>
    public IReadOnlyList<SavedQueue> List()
    {
        lock (_gate)
        {
            return Read().OrderByDescending(q => q.SavedAt).ToList();
        }
    }

    /// <summary>Saves a queue; one with the same name (ignoring case) is replaced.</summary>
    public void Save(string name, IReadOnlyList<SavedTrack> tracks, DateTimeOffset? now = null)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("A queue needs a name.", nameof(name));
        }

        lock (_gate)
        {
            var all = Read().Where(q => !q.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            all.Add(new SavedQueue(name, now ?? DateTimeOffset.Now, tracks.ToList()));
            Write(all);
        }
    }

    /// <summary>Deletes a queue; false when there was none of that name.</summary>
    public bool Delete(string name)
    {
        lock (_gate)
        {
            var all = Read();
            var kept = all.Where(q => !q.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (kept.Count == all.Count)
            {
                return false;
            }

            Write(kept);
            return true;
        }
    }

    private List<SavedQueue> Read()
    {
        if (!File.Exists(_path))
        {
            return new List<SavedQueue>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<SavedQueue>>(File.ReadAllText(_path), Json) ?? new List<SavedQueue>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            try
            {
                File.Move(_path, _path + ".bad", overwrite: true);
            }
            catch (IOException)
            {
                // Cannot even move it: leave it, the next save replaces it.
            }

            return new List<SavedQueue>();
        }
    }

    private void Write(List<SavedQueue> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(all, Json));
        File.Move(temp, _path, overwrite: true);
    }
}
