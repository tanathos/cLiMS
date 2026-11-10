namespace Clims.Lyrics;

/// <summary>
/// Lyrics in a file next to the track: <c>song.lrc</c> (timestamps) or <c>song.txt</c> (plain), named like the audio file; failing
/// that, any <c>.lrc</c> in the album folder whose name contains the song title as whole words. The server's file addresses
/// (<c>/mnt/Music/...</c>) are turned into paths this PC can open with a path map (<c>/mnt/Music</c> to <c>\\nas\Music</c>).
/// </summary>
public sealed class SidecarLyricsProvider : ILyricsProvider
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(8);
    private const int MaxBytes = 512 * 1024;
    private readonly IReadOnlyList<(string Server, string Local)> _map;
    private readonly string? _user;
    private readonly string _password;
    private readonly HashSet<string> _signedIn = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the provider.</summary>
    /// <param name="pathMap">Server folder to folder on this PC, for example <c>/mnt/Music</c> to <c>\\192.168.1.100\Music</c>. The longest matching server folder is used.</param>
    /// <param name="user">Login for a Windows network share named in the map (null: use whatever this PC is already signed in with).</param>
    /// <param name="password">The password for <paramref name="user"/>.</param>
    public SidecarLyricsProvider(IReadOnlyDictionary<string, string> pathMap, string? user = null, string? password = null)
    {
        _user = string.IsNullOrWhiteSpace(user) ? null : user.Trim();
        _password = password ?? string.Empty;
        _map = pathMap
            .Where(p => p.Key.Trim().Length > 0 && p.Value.Trim().Length > 0)
            .Select(p => (Server: p.Key.Trim().Replace('\\', '/').TrimEnd('/'), Local: p.Value.Trim()))
            .OrderByDescending(p => p.Server.Length)
            .ToList();
    }

    /// <inheritdoc />
    public string Name => "sidecar";

    /// <inheritdoc />
    public bool IsOnline => false;

    /// <summary>The folder on this PC that holds the track, or null when the address is unknown or no map entry covers it.</summary>
    public string? FolderOf(string? url, out string? baseName)
    {
        baseName = null;
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        var path = Uri.UnescapeDataString(url.Split('?', '#')[0]);
        if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            path = path["file://".Length..];
        }

        if (path.Length > 2 && path[0] == '/' && path[2] == ':')
        {
            path = path[1..]; // file:///C:/Music
        }

        path = path.Replace('\\', '/');
        foreach (var (server, local) in _map)
        {
            if (path.StartsWith(server + "/", StringComparison.Ordinal))
            {
                var parts = path[(server.Length + 1)..].Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    return null;
                }

                baseName = Path.GetFileNameWithoutExtension(parts[^1]);
                return Path.Combine([local, .. parts[..^1]]);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<LyricsResult?> FindAsync(LyricsQuery query, CancellationToken ct)
    {
        if (_map.Count == 0 || FolderOf(query.Url, out var baseName) is not { } folder || baseName is null)
        {
            return null;
        }

        // A share that is asleep or gone can block for a long time, and file reads cannot be cancelled: wait a while, then give up.
        // The error is IOException-like on purpose: "could not look", which the service does not remember as "has none".
        try
        {
            return await Task.Run(() => Look(folder, baseName, query.Title), CancellationToken.None).WaitAsync(Patience, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new IOException($"{folder} did not answer.");
        }
    }

    private void SignIn(string folder)
    {
        if (_user is null || ShareConnection.RootOf(folder) is not { } root)
        {
            return;
        }

        lock (_signedIn)
        {
            if (_signedIn.Contains(root))
            {
                return;
            }

            // A refusal throws, and is tried again with the next track: a wrong password fixed in settings needs a restart anyway,
            // but a NAS that was asleep may answer next time.
            ShareConnection.Connect(root, _user, _password);
            _signedIn.Add(root);
        }
    }

    private LyricsResult? Look(string folder, string baseName, string title)
    {
        SignIn(folder);
        if (!Directory.Exists(folder))
        {
            // Distinguish "no such album folder here" from "the share is not there": the map's root must exist.
            var root = _map.Select(m => m.Local).FirstOrDefault(l => folder.StartsWith(l, StringComparison.OrdinalIgnoreCase));
            if (root is not null && !Directory.Exists(root))
            {
                throw new IOException($"{root} cannot be reached.");
            }

            return null;
        }

        foreach (var extension in new[] { ".lrc", ".txt" })
        {
            if (Read(Path.Combine(folder, baseName + extension)) is { } text && LyricsText.From(text, Name) is { } same)
            {
                return same;
            }
        }

        var wanted = " " + LyricsKey.Fold(title) + " ";
        if (wanted.Length < 5)
        {
            return null;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.lrc"))
        {
            if ((" " + LyricsKey.Fold(Path.GetFileNameWithoutExtension(file)) + " ").Contains(wanted, StringComparison.Ordinal) &&
                Read(file) is { } text && LyricsText.From(text, Name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static string? Read(string file)
    {
        var info = new FileInfo(file);
        return info.Exists && info.Length is > 0 and <= MaxBytes ? File.ReadAllText(file) : null;
    }
}
