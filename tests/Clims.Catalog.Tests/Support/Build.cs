using System.Threading.Channels;
using Clims.Lms;

namespace Clims.Catalog.Tests;

public static class Build
{
    public static CatalogService Service(FakeLms lms, MemoryStore store, string? knownUuid = null, TimeSpan? scanPoll = null) =>
        new(lms, new CatalogServiceOptions
        {
            StoreFactory = _ => store,
            KnownServerUuid = knownUuid,
            ScanPollInterval = scanPoll ?? TimeSpan.FromMilliseconds(1),
            TrackPageSize = 5,
            Now = () => DateTimeOffset.FromUnixTimeSeconds(1_800_000_000),
        });

    /// <summary>A complete catalogue as a finished sync of <paramref name="lms"/> would leave it.</summary>
    public static Catalog SyncedCatalog(FakeLms lms)
    {
        var s = lms.Status();
        return new Catalog(
            lms.Genres.Select(g => new CatalogGenre(g.Id, g.Name)),
            lms.Artists.Select(a => new CatalogArtist(a.Id, a.Name)),
            lms.Albums.Select(a => new CatalogAlbum(a.Id, a.Title, a.ArtistId, a.ArtistName ?? "", a.Year, a.ArtworkTrackId, a.IsCompilation, a.DiscCount)),
            lms.Tracks.Select(t => new CatalogTrack(t.Id, t.AlbumId!.Value, t.Title, t.Artist, t.ArtistId, 1, t.TrackNumber ?? 0, t.Duration, t.Genre)),
            new CatalogMeta(s.Uuid ?? "", s.LastScan?.ToUnixTimeSeconds(), s.TrackCount, s.AlbumCount, s.ArtistCount, s.GenreCount, DateTimeOffset.FromUnixTimeSeconds(1_750_000_000)));
    }
}
