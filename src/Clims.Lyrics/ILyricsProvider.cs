namespace Clims.Lyrics;

/// <summary>A place lyrics can be looked up.</summary>
public interface ILyricsProvider
{
    /// <summary>Short stable name, used in settings.json and as the lyrics' source label.</summary>
    string Name { get; }

    /// <summary>True when asking sends the track's names over the internet. Local sources (tags, files next to the track) say false and work whatever the user decided about online lookup.</summary>
    bool IsOnline => true;

    /// <summary>
    /// The lyrics, or null when the provider does not have this track. Throws <see cref="HttpRequestException"/> (or a
    /// timeout) when the provider could not be asked, which is not the same as not having it: nothing is then remembered.
    /// </summary>
    Task<LyricsResult?> FindAsync(LyricsQuery query, CancellationToken ct);
}
