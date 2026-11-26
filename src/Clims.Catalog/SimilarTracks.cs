namespace Clims.Catalog;

/// <summary>
/// A random pick of tracks like a selection (one track, or the tracks of an album). Genres decide first: a track with all
/// of the selection's genres comes before one with half of them, and that before one with fewer; one with none is never
/// picked. The year decides within that, and chance within the year. Albums and artists are limited, for variety.
/// </summary>
public static class SimilarTracks
{
    /// <summary>Most tracks taken from one album, unless the list would otherwise be short.</summary>
    public const int PerAlbum = 2;

    /// <summary>Most tracks taken of one artist, unless the list would otherwise be short.</summary>
    public const int PerArtist = 4;

    /// <summary>Share of the selection's genre weight that counts as all of it: a genre of one stray track is not required.</summary>
    private const double Full = 0.9;

    private const double Half = 0.5;

    /// <summary>Year distances up to which a track is in the first, second... year group; farther and unknown years come last.</summary>
    private static readonly int[] YearBands = [0, 2, 5, 10];

    /// <summary>True when any track of the selection has a genre; without one nothing is similar to it.</summary>
    public static bool HasGenre(Catalog catalog, IReadOnlyList<CatalogTrack> seed) => seed.Any(t => catalog.GenresOfTrack(t).Count > 0);

    /// <summary>Up to <paramref name="count"/> tracks similar to the selection, in a random order.</summary>
    /// <param name="catalog">The library to pick from.</param>
    /// <param name="year">Release year of the selection, 0 when unknown (then years do not matter).</param>
    /// <param name="seed">The selected track, or the tracks of the selected album. They are not picked.</param>
    /// <param name="count">How many tracks are wanted.</param>
    /// <param name="exclude">Other track ids not to pick (those already queued).</param>
    /// <param name="random">The source of chance.</param>
    public static IReadOnlyList<(CatalogAlbum Album, CatalogTrack Track)> Pick(
        Catalog catalog, int year, IReadOnlyList<CatalogTrack> seed, int count, IEnumerable<int> exclude, Random random)
    {
        // Each genre of the selection weighs as much as the share of its tracks that have it.
        var weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in seed)
        {
            foreach (var genre in catalog.GenresOfTrack(track).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                weights[genre] = weights.GetValueOrDefault(genre) + 1.0 / seed.Count;
            }
        }

        var total = weights.Values.Sum();
        if (total <= 0 || count <= 0)
        {
            return [];
        }

        var excluded = exclude.Concat(seed.Select(t => t.Id)).ToHashSet();
        var candidates = new List<(int Genre, int Year, double Chance, CatalogAlbum Album, CatalogTrack Track)>();
        foreach (var track in catalog.SnapshotTracks())
        {
            if (excluded.Contains(track.Id) || catalog.Album(track.AlbumId) is not { } album)
            {
                continue;
            }

            var shared = catalog.GenresOfTrack(track).Distinct(StringComparer.OrdinalIgnoreCase).Sum(g => weights.GetValueOrDefault(g));
            if (shared > 0)
            {
                var share = shared / total;
                candidates.Add((share >= Full ? 0 : share >= Half ? 1 : 2, YearGroup(year, album.Year), random.NextDouble(), album, track));
            }
        }

        var picked = new List<(CatalogAlbum Album, CatalogTrack Track)>(count);
        var passedOver = new List<(CatalogAlbum Album, CatalogTrack Track)>();
        var ofAlbum = new Dictionary<int, int>();
        var ofArtist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in candidates.OrderBy(c => c.Genre).ThenBy(c => c.Year).ThenBy(c => c.Chance))
        {
            if (picked.Count == count)
            {
                break;
            }

            var artist = ArtistOf(c.Album, c.Track);
            if (ofAlbum.GetValueOrDefault(c.Album.Id) >= PerAlbum || (artist.Length > 0 && ofArtist.GetValueOrDefault(artist) >= PerArtist))
            {
                passedOver.Add((c.Album, c.Track));
                continue;
            }

            ofAlbum[c.Album.Id] = ofAlbum.GetValueOrDefault(c.Album.Id) + 1;
            ofArtist[artist] = ofArtist.GetValueOrDefault(artist) + 1;
            picked.Add((c.Album, c.Track));
        }

        // Too few within the limits: the best of those passed over make up the number.
        picked.AddRange(passedOver.Take(count - picked.Count));
        return Mix(picked, random);
    }

    private static int YearGroup(int wanted, int year)
    {
        if (wanted == 0)
        {
            return 0;
        }

        var group = year == 0 ? -1 : Array.FindIndex(YearBands, band => Math.Abs(year - wanted) <= band);
        return group < 0 ? YearBands.Length : group;
    }

    private static string ArtistOf(CatalogAlbum album, CatalogTrack track) =>
        string.IsNullOrWhiteSpace(track.Artist) ? album.ArtistName : track.Artist;

    /// <summary>Shuffles, then takes the next track of another artist than the one before wherever there is one.</summary>
    private static List<(CatalogAlbum Album, CatalogTrack Track)> Mix(List<(CatalogAlbum Album, CatalogTrack Track)> tracks, Random random)
    {
        var left = tracks.OrderBy(_ => random.NextDouble()).ToList();
        var mixed = new List<(CatalogAlbum Album, CatalogTrack Track)>(left.Count);
        var last = string.Empty;
        while (left.Count > 0)
        {
            var next = Math.Max(0, left.FindIndex(x => !string.Equals(ArtistOf(x.Album, x.Track), last, StringComparison.OrdinalIgnoreCase)));
            last = ArtistOf(left[next].Album, left[next].Track);
            mixed.Add(left[next]);
            left.RemoveAt(next);
        }

        return mixed;
    }
}
