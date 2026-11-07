using Clims.Catalog;
using Clims.Lms;

namespace Clims.Tui.Data;

/// <summary>Deterministic fake library (1 000 artists, about 2 500 albums) for trying the UI without a server (<c>--demo</c>).</summary>
public static class DemoCatalog
{
    private static readonly string[] Prefix =
    {
        "Black", "Silent", "Burning", "Frozen", "Hollow", "Crimson", "Ancient", "Velvet", "Iron", "Pale",
        "Electric", "Lunar", "Wild", "Distant", "Broken", "Golden", "Neon", "Shadow", "Quiet", "Savage",
    };

    private static readonly string[] Noun =
    {
        "Wolves", "Garden", "Machine", "Echo", "Season", "Harbor", "Choir", "Engine", "Mirror", "Tide",
        "Orchard", "Signal", "Empire", "Ritual", "Horizon", "Archive", "Station", "Cathedral", "Rain", "Fire",
    };

    private static readonly string[] GenreNames =
    {
        "Black Metal", "Folk Metal", "Post-Rock", "Ambient", "Jazz", "Progressive Rock", "Electronic", "Doom Metal",
        "Classical", "Synthwave", "Dream Pop", "Blues",
    };

    private static readonly string[] Word =
    {
        "Birth", "Death", "Serpent", "Sphere", "Vales", "Beyond", "Ashes", "Winter", "Cor", "Light", "Dust", "Memory",
        "River", "Sleep", "Forest", "Altar", "Glass", "Salt", "Thunder", "Moon",
    };

    public static MusicCatalog Create()
    {
        var rnd = new Random(42);
        var names = new HashSet<string>();
        for (var i = 0; names.Count < 1000; i++)
        {
            var name = $"{Prefix[rnd.Next(Prefix.Length)]} {Noun[rnd.Next(Noun.Length)]}";
            if (rnd.Next(4) == 0)
            {
                name = "The " + name;
            }

            if (!names.Add(name))
            {
                names.Add($"{name} {i}");
            }
        }

        var sorted = names.OrderBy(n => n.StartsWith("The ") ? n[4..] : n, StringComparer.OrdinalIgnoreCase).ToList();
        var artists = new List<CatalogArtist>();
        var albums = new List<CatalogAlbum>();
        var tracks = new List<CatalogTrack>();
        var albumId = 1;
        var trackId = 1;
        for (var a = 0; a < sorted.Count; a++)
        {
            artists.Add(new CatalogArtist(a + 1, sorted[a]));
            var count = 1 + rnd.Next(4);
            for (var k = 0; k < count; k++)
            {
                var id = albumId++;
                albums.Add(new CatalogAlbum(id, $"{Word[rnd.Next(Word.Length)]} and the {Word[rnd.Next(Word.Length)]}", a + 1, sorted[a], 1975 + rnd.Next(50), null, false, 1));
                var genre = GenreNames[rnd.Next(GenreNames.Length)];
                var second = rnd.Next(2) == 0 ? GenreNames[rnd.Next(GenreNames.Length)] : null;
                var n = 6 + rnd.Next(7);
                for (var t = 1; t <= n; t++)
                {
                    tracks.Add(new CatalogTrack(
                        trackId++,
                        id,
                        $"{Word[rnd.Next(Word.Length)]} {Word[rnd.Next(Word.Length)]}",
                        sorted[a],
                        a + 1,
                        1,
                        t,
                        TimeSpan.FromSeconds(120 + rnd.Next(480)),
                        second is not null && t % 3 == 0 ? second : genre));
                }
            }
        }

        var genres = GenreNames.Select((g, i) => new CatalogGenre(i + 1, g)).ToList();
        var meta = new CatalogMeta("demo", null, tracks.Count, albums.Count, artists.Count, genres.Count, DateTimeOffset.UtcNow);
        return new MusicCatalog(genres, artists, albums, tracks, meta);
    }
}
