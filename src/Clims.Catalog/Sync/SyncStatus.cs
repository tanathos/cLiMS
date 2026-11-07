using Clims.Lms;

namespace Clims.Catalog;

/// <summary>What to show in the header.</summary>
/// <param name="State">Current state.</param>
/// <param name="HasIndex">True when a usable index is loaded (so the Library tab has data).</param>
/// <param name="Progress">Progress text while syncing, for example <c>artists ✓ albums ✓ tracks 42%</c>.</param>
/// <param name="SyncedAt">When the loaded index was built.</param>
/// <param name="Detail">Why the state is what it is (what changed, or the last error).</param>
public sealed record SyncStatus(SyncState State, bool HasIndex, string? Progress, DateTimeOffset? SyncedAt, string? Detail)
{
    /// <summary>The state before anything is known.</summary>
    public static SyncStatus Initial { get; } = new(SyncState.NotSynced, false, null, null, null);
}
