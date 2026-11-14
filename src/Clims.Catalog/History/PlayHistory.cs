namespace Clims.Catalog;

/// <summary>What the play log means: which tracks were played lately, and which albums were listened to and how many times.</summary>
public static class PlayHistory
{
    /// <summary>The plays since <paramref name="since"/>, newest first.</summary>
    public static IReadOnlyList<PlayRecord> Tracks(IEnumerable<PlayRecord> plays, DateTimeOffset since) =>
        plays.Where(p => p.PlayedAt >= since).OrderByDescending(p => p.PlayedAt).ToList();

    /// <summary>
    /// What names an album across a rebuild of the server's library (LMS may hand out new ids): its artist and title, without case or accents.
    /// </summary>
    public static string AlbumKey(string artist, string album) => TextFolding.Fold(artist) + "|" + TextFolding.Fold(album);

    /// <summary>How many different tracks make a listen of an album: <paramref name="percent"/> of them (50 by default), rounded up.</summary>
    public static int Needed(int albumTracks, int percent = 50) => Math.Max(1, (int)Math.Ceiling(Math.Max(1, albumTracks) * Math.Clamp(percent, 1, 100) / 100.0));

    /// <summary>
    /// The albums with at least one listen finished since <paramref name="since"/>, the latest first. A listen is a run of plays of
    /// one album that reached <paramref name="percent"/> of its tracks; a track heard twice inside a run starts the next run. Albums and tracks are told apart by name, so plays logged before a rebuild of the server's library still count with the ones after it.
    /// </summary>
    public static IReadOnlyList<AlbumListen> Albums(IEnumerable<PlayRecord> plays, DateTimeOffset since, int percent = 50)
    {
        var result = new List<AlbumListen>();
        foreach (var group in plays.GroupBy(p => AlbumKey(p.Artist, p.Album)))
        {
            var ordered = group.OrderBy(p => p.PlayedAt).ToList();
            var last = ordered[^1];
            var needed = Needed(ordered.Max(p => p.AlbumTracks), percent); // a play logged before the tracks were known has 0
            var seen = new HashSet<string>();
            var finished = new List<DateTimeOffset>();
            foreach (var play in ordered)
            {
                var track = TextFolding.Fold(play.Title); // by name: track ids change too
                if (!seen.Add(track))
                {
                    seen.Clear();
                    seen.Add(track);
                }

                if (seen.Count >= needed)
                {
                    finished.Add(play.PlayedAt);
                    seen.Clear();
                }
            }

            var inWindow = finished.Count(t => t >= since);
            if (inWindow > 0)
            {
                result.Add(new AlbumListen(last.AlbumId, last.Album, last.Artist, finished[^1], inWindow, finished.Count));
            }
        }

        return result.OrderByDescending(a => a.LastListen).ToList();
    }
}
