namespace Clims.Tui;

/// <summary>
/// The "Lyrics" node of settings.json. Lyrics already stored are always shown; <see cref="OnlineLookup"/> decides whether
/// missing ones are searched on the internet. Missing values take these defaults.
/// </summary>
public sealed record LyricsSettings
{
    /// <summary>The providers a name in <see cref="Providers"/> can refer to, in the order they are asked by default.</summary>
    public static readonly IReadOnlyList<string> KnownProviders = ["lrclib", "darklyrics", "lyricsovh"];

    /// <summary>False: no lyrics at all (nothing is shown, stored or searched).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// True: lyrics missing from the store are searched with <see cref="Providers"/>, which sends the artist, album, title
    /// and length of the playing track to those sites. Off until the user turns it on (key L in the album view, or here).
    /// </summary>
    public bool OnlineLookup { get; init; }

    /// <summary>
    /// Providers asked, in order: "lrclib" (synced, the best), "darklyrics" (metal, plain text, one page per album),
    /// "lyricsovh" (plain text). Unknown names are ignored.
    /// </summary>
    public List<string> Providers { get; init; } = ["lrclib"];

    /// <summary>
    /// Lets lyrics be read from files next to the tracks (<c>song.lrc</c>, <c>song.txt</c>): the server's folder, as it appears in the
    /// tracks' file addresses, to the same folder as this PC sees it. Example: <c>{ "/mnt/Music": "\\\\192.168.1.100\\Music" }</c>.
    /// Empty: no such files are looked for. (The lyrics inside the tracks' tags need nothing: the server hands them out.)
    /// </summary>
    public Dictionary<string, string> MusicPathMap { get; init; } = new();

    /// <summary>
    /// Login for a Windows network share in <see cref="MusicPathMap"/> (<c>user</c> or <c>NAS\user</c>). Null: use the login this PC
    /// already has for it (for example the one Explorer remembers).
    /// </summary>
    public string? MusicShareUser { get; init; }

    /// <summary>
    /// The share's password, typed here as plain text. At the next launch cLiMS encrypts it into <see cref="MusicShareProtectedPassword"/>
    /// and removes this value from the file.
    /// </summary>
    public string? MusicSharePassword { get; init; }

    /// <summary>The share's password, encrypted with <see cref="SecretStore"/> (readable only by this Windows user on this PC).</summary>
    public string? MusicShareProtectedPassword { get; init; }

    /// <summary>Days a "not found" is believed before the track is searched again (0: search every time).</summary>
    public int MissRetryDays { get; init; } = 1;
}
