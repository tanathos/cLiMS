namespace Clims.Catalog;

/// <summary>
/// The parsed query: words folded (case, accents), plus the same words joined by single spaces. Names are normalised the
/// same way ("AC/DC - Back in Black" becomes <c>ac dc back in black</c>), so matching is plain string work.
/// </summary>
public sealed class SearchQuery
{
    private SearchQuery(string[] words)
    {
        Words = words;
        Text = string.Join(' ', words);
    }

    /// <summary>Folded query words.</summary>
    public string[] Words { get; }

    /// <summary>The words joined with single spaces.</summary>
    public string Text { get; }

    /// <summary>True when the query has no words.</summary>
    public bool IsEmpty => Words.Length == 0;

    /// <summary>Parses user input.</summary>
    public static SearchQuery Parse(string text) => new(TextFolding.Words(TextFolding.Fold(text ?? string.Empty)));
}

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

/// <summary>
/// The ranking rules. A name matches when every query word occurs in it, anywhere (the server only matches word starts).
/// Better matches score higher:
/// <list type="bullet">
/// <item>100: the name is exactly the query.</item>
/// <item>80: the name starts with the query.</item>
/// <item>60: every query word starts a word of the name.</item>
/// <item>30: every query word occurs somewhere in the name.</item>
/// </list>
/// Albums and tracks that match only through their artist or album name score half of that tier; a title match whose
/// artist also matches the query well gets a small boost, so "iron maiden" puts Iron Maiden's own songs first.
/// Ties are broken by the shorter name, then alphabetically.
/// </summary>
public static class SearchRanking
{
    /// <summary>Score of one normalised text for a query: 0 means no match.</summary>
    public static int Tier(string normalized, SearchQuery query)
    {
        if (query.IsEmpty)
        {
            return 0;
        }

        var allWordStarts = true;
        foreach (var word in query.Words)
        {
            var at = normalized.IndexOf(word, StringComparison.Ordinal);
            if (at < 0)
            {
                return 0;
            }

            if (allWordStarts && !StartsAWord(normalized, word, at))
            {
                allWordStarts = false;
            }
        }

        if (normalized.Length == query.Text.Length && normalized == query.Text)
        {
            return 100;
        }

        if (normalized.StartsWith(query.Text, StringComparison.Ordinal))
        {
            return 80;
        }

        return allWordStarts ? 60 : 30;
    }

    /// <summary>Score of an item with its three forms, 0 for no match.</summary>
    public static int Score(in SearchKeys keys, SearchQuery query)
    {
        var tier = Tier(keys.Name, query);
        if (tier > 0)
        {
            // The artist boost: the artist is also what the user typed.
            return keys.Artist is { Length: > 0 } artist && Tier(artist, query) >= 80 ? tier + 5 : tier;
        }

        return keys.Artist is null ? 0 : Tier(keys.All, query) / 2;
    }

    /// <summary>
    /// Ranks <paramref name="items"/> (their keys in <paramref name="keys"/>, same order) and keeps the best
    /// <paramref name="max"/>. <see cref="SearchGroup{T}.Total"/> counts every match.
    /// </summary>
    public static SearchGroup<T> Rank<T>(IReadOnlyList<T> items, IReadOnlyList<SearchKeys> keys, SearchQuery query, int max)
    {
        if (query.IsEmpty || items.Count == 0)
        {
            return SearchGroup<T>.None;
        }

        var hits = new List<(int Score, int Index)>();
        for (var i = 0; i < items.Count; i++)
        {
            var score = Score(keys[i], query);
            if (score > 0)
            {
                hits.Add((score, i));
            }
        }

        hits.Sort((a, b) =>
        {
            var c = b.Score.CompareTo(a.Score);
            if (c != 0)
            {
                return c;
            }

            var na = keys[a.Index].Name;
            var nb = keys[b.Index].Name;
            c = na.Length.CompareTo(nb.Length);
            if (c != 0)
            {
                return c;
            }

            c = string.CompareOrdinal(na, nb);
            return c != 0 ? c : a.Index.CompareTo(b.Index);
        });

        return new SearchGroup<T>(hits.Take(max).Select(h => items[h.Index]).ToList(), hits.Count);
    }

    private static bool StartsAWord(string text, string word, int firstAt)
    {
        var at = firstAt;
        while (at >= 0)
        {
            if (at == 0 || text[at - 1] == ' ')
            {
                return true;
            }

            at = text.IndexOf(word, at + 1, StringComparison.Ordinal);
        }

        return false;
    }
}
