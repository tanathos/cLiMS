using Clims.Lms;

namespace Clims.Catalog;

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
