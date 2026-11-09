namespace Clims.Lyrics;

/// <summary>The lyrics of one track, as text and, when a provider has them, with timestamps.</summary>
/// <param name="Plain">The words, lines separated by newlines (empty for an instrumental).</param>
/// <param name="SyncedLrc">The same in LRC format (<c>[mm:ss.xx]words</c>), or null when there are no timestamps.</param>
/// <param name="Source">Where it came from: a provider name, or "manual".</param>
/// <param name="Instrumental">True when the track is known to have no words.</param>
public sealed record LyricsResult(string Plain, string? SyncedLrc, string Source, bool Instrumental = false)
{
    private IReadOnlyList<LyricLine>? _lines;

    /// <summary>The timestamped lines, or an empty list when the lyrics are plain only.</summary>
    public IReadOnlyList<LyricLine> Lines => _lines ??= SyncedLrc is { Length: > 0 } ? LrcParser.Parse(SyncedLrc) : [];

    /// <summary>True when there are timestamped lines.</summary>
    public bool IsSynced => Lines.Count > 0;

    /// <summary>Other tracks the provider found on the way (an album page holds them all), stored too so they need no search later.</summary>
    public IReadOnlyList<(LyricsQuery Query, LyricsResult Result)> Siblings { get; init; } = [];
}
