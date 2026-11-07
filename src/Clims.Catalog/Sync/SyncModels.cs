using Clims.Lms;

namespace Clims.Catalog;

/// <summary>Where the local index stands compared with the server. The five states of <c>search.md</c> section 10, plus a demo state.</summary>
public enum SyncState
{
    /// <summary>No index on disk and no sync running.</summary>
    NotSynced,

    /// <summary>A sync is running; the Library tab shows what is loaded so far.</summary>
    Syncing,

    /// <summary>The index matches the server's last scan and counts.</summary>
    Synced,

    /// <summary>The server's last scan or counts moved; the old index keeps serving until the user agrees to resync.</summary>
    ServerChanged,

    /// <summary>The server cannot be reached; browsing uses the saved index, playback does not work.</summary>
    Offline,

    /// <summary>The server answered but rejected the login (or wants one). Nothing is retried until the user fixes it.</summary>
    LoginRequired,

    /// <summary>No server at all: built-in fake data for trying the UI.</summary>
    Demo,
}

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

/// <summary>Why the user is being asked whether to sync.</summary>
public enum SyncPromptReason
{
    /// <summary>There is no index yet.</summary>
    FirstBuild,

    /// <summary>The server's last scan or counts changed since the index was built.</summary>
    ServerChanged,
}

/// <summary>A request to ask the user whether to sync now. The library never starts a sync on its own.</summary>
/// <param name="Reason">Why.</param>
/// <param name="Detail">Human readable detail (for example what changed).</param>
public sealed record SyncPrompt(SyncPromptReason Reason, string Detail);

/// <summary>Outcome of comparing an index with the live server.</summary>
public enum AssessmentKind
{
    /// <summary>No index.</summary>
    NoIndex,

    /// <summary>Nothing changed.</summary>
    UpToDate,

    /// <summary>The server moved on.</summary>
    Changed,
}

/// <summary>Result of <see cref="SyncAssessor.Assess"/>.</summary>
/// <param name="Kind">The outcome.</param>
/// <param name="Reason">What changed, for <see cref="AssessmentKind.Changed"/>.</param>
public sealed record Assessment(AssessmentKind Kind, string? Reason = null);

/// <summary>
/// Decides whether a resync is worth proposing. A finished scan by itself is not a reason (the server sends
/// <c>rescan done</c> even when nothing changed): only a moved <c>lastscan</c> or different counts are.
/// </summary>
public static class SyncAssessor
{
    /// <summary>Compares the index metadata with the live server status.</summary>
    public static Assessment Assess(CatalogMeta? meta, ServerStatus server)
    {
        if (meta is null)
        {
            return new Assessment(AssessmentKind.NoIndex);
        }

        var reasons = new List<string>();
        var scan = server.LastScan?.ToUnixTimeSeconds();
        if (scan != meta.LastScan)
        {
            reasons.Add("the library was rescanned");
        }

        void Count(string name, int then, int now)
        {
            if (then != now)
            {
                reasons.Add($"{name} {then} to {now}");
            }
        }

        Count("tracks", meta.TrackCount, server.TrackCount);
        Count("albums", meta.AlbumCount, server.AlbumCount);
        Count("artists", meta.ArtistCount, server.ArtistCount);
        Count("genres", meta.GenreCount, server.GenreCount);

        return reasons.Count == 0
            ? new Assessment(AssessmentKind.UpToDate)
            : new Assessment(AssessmentKind.Changed, string.Join(", ", reasons));
    }
}
