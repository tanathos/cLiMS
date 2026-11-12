namespace Clims.Tui.Settings;

/// <summary>The "Lyrics.Files" node of a server: lyrics kept in files next to the tracks.</summary>
public sealed record LyricsFilesSettings
{
    /// <summary>
    /// The server's folder, as it appears in the tracks' file addresses, to the same folder as this PC sees it
    /// (<c>{ "/mnt/Music": "\\\\192.168.1.100\\Music" }</c>). Empty: no such files are looked for.
    /// </summary>
    public Dictionary<string, string> PathMap { get; init; } = new();

    /// <summary>Login for a Windows network share in <see cref="PathMap"/>. Null: the login this PC already has for it.</summary>
    public string? ShareUser { get; init; }

    /// <summary>A share password typed here as plain text; it is encrypted into <see cref="ProtectedSharePassword"/> and removed from the file.</summary>
    public string? SharePassword { get; init; }

    /// <summary>The share's password, encrypted with <see cref="SecretStore"/>.</summary>
    public string? ProtectedSharePassword { get; init; }
}
