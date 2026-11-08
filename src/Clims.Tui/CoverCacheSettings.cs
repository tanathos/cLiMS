namespace Clims.Tui;

/// <summary>
/// The "CoverCache" node of settings.json: covers are kept in a SQLite file of their own (covers-&lt;server uuid&gt;.db) and
/// filled lazily, as views show them. Missing values take these defaults.
/// </summary>
public sealed record CoverCacheSettings
{
    /// <summary>False: covers are always fetched from the server (kept in memory only).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Covers in lists and the player (300 px).</summary>
    public CoverCacheTier Normal { get; init; } = new(30);

    /// <summary>The full-screen cover (900 px, about 144 KB each), so by default only the last 50 are kept.</summary>
    public CoverCacheTier Big { get; init; } = new(30, 50);
}
