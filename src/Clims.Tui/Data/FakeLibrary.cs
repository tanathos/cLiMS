namespace Clims.Tui.Data;

/// <summary>Deterministic in-memory catalogue: 1 000 artists, about 2 500 albums.</summary>
public sealed class FakeLibrary : ILibrarySource
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

    private static readonly string[] Genre =
    {
        "Black Metal", "Folk Metal", "Post-Rock", "Ambient", "Jazz", "Progressive Rock", "Electronic", "Doom Metal",
        "Classical", "Synthwave", "Dream Pop", "Blues",
    };

    private static readonly string[] Word =
    {
        "Birth", "Death", "Serpent", "Sphere", "Vales", "Beyond", "Ashes", "Winter", "Cor", "Light", "Dust", "Memory",
        "River", "Sleep", "Forest", "Altar", "Glass", "Salt", "Thunder", "Moon",
    };

    private readonly List<ArtistItem> _artists = new();
    private readonly Dictionary<int, List<AlbumItem>> _albums = new();
    private readonly List<AlbumItem> _allAlbums = new();
    private readonly Dictionary<int, AlbumItem> _trackAlbum = new();

    public FakeLibrary()
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
        var albumId = 1;
        var trackId = 1;
        for (var a = 0; a < sorted.Count; a++)
        {
            var count = 1 + rnd.Next(4);
            var list = new List<AlbumItem>();
            for (var k = 0; k < count; k++)
            {
                var title = $"{Word[rnd.Next(Word.Length)]} and the {Word[rnd.Next(Word.Length)]}";
                var tracks = new List<TrackItem>();
                var n = 6 + rnd.Next(7);
                var total = TimeSpan.Zero;
                for (var t = 1; t <= n; t++)
                {
                    var len = TimeSpan.FromSeconds(120 + rnd.Next(480));
                    total += len;
                    tracks.Add(new TrackItem(trackId++, t, $"{Word[rnd.Next(Word.Length)]} {Word[rnd.Next(Word.Length)]}", sorted[a], title, len));
                }

                var genres = Genre[rnd.Next(Genre.Length)] + (rnd.Next(2) == 0 ? "; " + Genre[rnd.Next(Genre.Length)] : "");
                var album = new AlbumItem(albumId++, title, sorted[a], 1975 + rnd.Next(50), genres, total, rnd.Next(), tracks);
                list.Add(album);
                _allAlbums.Add(album);
                foreach (var track in tracks)
                {
                    _trackAlbum[track.Id] = album;
                }
            }

            _albums[a + 1] = list.OrderBy(x => x.Year).ToList();
            _artists.Add(new ArtistItem(a + 1, sorted[a], count));
        }
    }

    public IReadOnlyList<ArtistItem> Artists => _artists;

    public AlbumItem FirstAlbum => _allAlbums[0];

    /// <summary>Artist id 0 means "All Artists".</summary>
    public IReadOnlyList<AlbumItem> GetAlbums(int artistId) =>
        artistId == 0 ? _allAlbums
        : _albums.TryGetValue(artistId, out var l) ? l : Array.Empty<AlbumItem>();

    public AlbumItem? GetAlbumOfTrack(int trackId) =>
        _trackAlbum.TryGetValue(trackId, out var album) ? album : null;

    public ArtistItem? GetArtist(string name) => _artists.FirstOrDefault(a => a.Name == name);

    public SearchResults Search(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return new SearchResults(Array.Empty<ArtistItem>(), Array.Empty<AlbumItem>(), Array.Empty<TrackItem>());
        }

        static bool Match(string s, string[] w) =>
            w.All(x => s.Split(' ').Any(p => p.StartsWith(x, StringComparison.OrdinalIgnoreCase)));

        var artists = _artists.Where(a => Match(a.Name, words)).Take(20).ToList();
        var albums = _allAlbums.Where(a => Match(a.Title + " " + a.Artist, words)).Take(40).ToList();
        var tracks = _allAlbums.SelectMany(a => a.Tracks).Where(t => Match(t.Title + " " + t.Artist, words)).Take(100).ToList();
        return new SearchResults(artists, albums, tracks);
    }
}
