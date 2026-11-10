namespace Clims.Tui.Settings;

/// <summary>The "Lyrics" node of a server. Lyrics already stored are always shown; <see cref="OnlineLookup"/> decides whether missing ones are searched online.</summary>
public sealed record ServerLyricsSettings
{
    /// <summary>False: no lyrics at all.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>True: missing lyrics are searched with <see cref="Providers"/>, which sends artist, album, title and length of the playing track to those sites.</summary>
    public bool OnlineLookup { get; init; }

    /// <summary>Providers asked, in order (see <see cref="LyricsSettings.KnownProviders"/>). Unknown names are ignored.</summary>
    public List<string> Providers { get; init; } = ["lrclib"];

    /// <summary>Days a "not found" is believed before the track is searched again (0: every time).</summary>
    public int MissRetryDays { get; init; } = 1;

    public LyricsFilesSettings Files { get; init; } = new();
}
