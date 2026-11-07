namespace Clims.Catalog;

/// <summary>The searchable forms of one item: its main name, its artist, and everything searchable joined.</summary>
/// <param name="Name">Normalised main name (artist, album or track title, genre).</param>
/// <param name="Artist">Normalised artist name (albums and tracks), or null.</param>
/// <param name="All">Name, artist and album title joined, so "wish cure" finds the album Wish by The Cure.</param>
public readonly record struct SearchKeys(string Name, string? Artist, string All)
{
    /// <summary>Keys of an item that has only a name.</summary>
    public static SearchKeys ForName(string name)
    {
        var n = Normalize(name);
        return new SearchKeys(n, null, n);
    }

    /// <summary>Keys of an album.</summary>
    public static SearchKeys ForAlbum(string title, string artist)
    {
        var t = Normalize(title);
        var a = Normalize(artist);
        return new SearchKeys(t, a, t + " " + a);
    }

    /// <summary>Keys of a track.</summary>
    public static SearchKeys ForTrack(string title, string? artist, string? album)
    {
        var t = Normalize(title);
        var a = artist is null ? null : Normalize(artist);
        var l = album is null ? "" : Normalize(album);
        return new SearchKeys(t, a, t + " " + (a ?? "") + " " + l);
    }

    private static string Normalize(string s) => string.Join(' ', TextFolding.Words(TextFolding.Fold(s)));
}
