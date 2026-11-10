namespace Clims.Tui;

/// <summary>Cache rules for one cover resolution.</summary>
/// <param name="TtlDays">Days a stored cover is trusted before it is fetched again. Zero or less: this resolution is not cached.</param>
/// <param name="MaxEntries">Most covers of this resolution kept (the newest). Zero: no limit.</param>
public sealed record CoverCacheTier(int TtlDays = 30, int MaxEntries = 0)
{
    /// <summary>True when covers of this resolution are stored at all.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Active => TtlDays > 0;

    /// <summary>The time to live as a span.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TimeSpan Ttl => TimeSpan.FromDays(Math.Max(0, TtlDays));
}
