namespace Clims.Lyrics;

/// <summary>Builds DarkLyrics page addresses: <c>/lyrics/&lt;artist&gt;/&lt;album&gt;.html</c>, both lower case with every space and symbol removed.</summary>
public static class DarkLyricsUrl
{
    /// <summary>The album page addresses worth trying, most likely first (the same artist with and without a leading "The").</summary>
    public static IReadOnlyList<string> For(string artist, string album)
    {
        var albumSlug = Slug(album);
        var artistSlug = Slug(artist);
        if (albumSlug.Length == 0 || artistSlug.Length == 0)
        {
            return [];
        }

        var slugs = new List<string> { artistSlug };
        var folded = LyricsKey.Fold(artist);
        if (folded.StartsWith("the ", StringComparison.Ordinal) && Slug(folded[4..]) is { Length: > 0 } without)
        {
            slugs.Add(without);
        }

        return slugs.Select(s => $"http://www.darklyrics.com/lyrics/{s}/{albumSlug}.html").ToList();
    }

    /// <summary>Lower case letters and digits only.</summary>
    public static string Slug(string text) => LyricsKey.Fold(text).Replace(" ", "", StringComparison.Ordinal);
}
