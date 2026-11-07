using Clims.Lms;

namespace Clims.Catalog;

/// <summary>Settings for <see cref="CatalogService"/>.</summary>
public sealed class CatalogServiceOptions
{
    /// <summary>Server UUID from a previous run, so the saved index can be used while the server is unreachable.</summary>
    public string? KnownServerUuid { get; init; }

    /// <summary>Creates the store for a server UUID.</summary>
    public required Func<string, ICatalogStore> StoreFactory { get; init; }

    /// <summary>How often to ask whether the server is still scanning before a sync can continue.</summary>
    public TimeSpan ScanPollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Tracks per request during a sync (the server fails above about 5 000).</summary>
    public int TrackPageSize { get; init; } = 2000;

    /// <summary>Clock, replaceable in tests.</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;
}
