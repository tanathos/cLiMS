namespace Clims.Tui.Data;

/// <summary>Where the lyrics of the playing track stand.</summary>
public enum LyricsState
{
    /// <summary>Nothing is playing.</summary>
    Idle,

    /// <summary>A search is running.</summary>
    Loading,

    /// <summary>Lyrics are available.</summary>
    Found,

    /// <summary>The search found nothing.</summary>
    NotFound,

    /// <summary>Nothing is stored and online lookup is off.</summary>
    Off,
}
