namespace Clims.Tui.Settings;

/// <summary>The "Cache" node: what is kept on this PC. The cache files are per server, the rules are shared.</summary>
public sealed record CacheSettings
{
    public CoverCacheSettings Covers { get; init; } = new();
}
