namespace Clims.Tui.Settings;

/// <summary>One server in the "Servers" list, with the settings that belong to it.</summary>
public sealed record ServerSettings
{
    /// <summary>The name shown in the server list.</summary>
    public string? Name { get; init; }

    public string? Url { get; init; }

    /// <summary>The server's UUID, so that its saved index can be used while it is unreachable.</summary>
    public string? Uuid { get; init; }

    public string? Username { get; init; }

    /// <summary>The password, protected by <see cref="SecretStore"/> (never plain text).</summary>
    public string? ProtectedPassword { get; init; }

    /// <summary>The player to control (usually its MAC address). Null: the first connected hardware player.</summary>
    public string? PlayerId { get; init; }

    public ServerLyricsSettings Lyrics { get; init; } = new();

    /// <summary>True when <paramref name="key"/> is this server's UUID or address.</summary>
    public bool Matches(string? key) =>
        !string.IsNullOrEmpty(key) && (string.Equals(Uuid, key, StringComparison.OrdinalIgnoreCase) || string.Equals(Url, key, StringComparison.OrdinalIgnoreCase));
}
