using System.Globalization;
using System.Text;

namespace Clims.Catalog;

/// <summary>Case and accent folding used for matching and A-Z keys ("Pantéon" matches "pante", like the server does).</summary>
public static class TextFolding
{
    /// <summary>Lower-cases and strips diacritics.</summary>
    public static string Fold(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Where the query words match in <paramref name="text"/>, as (start, length) ranges of the original string, sorted and
    /// merged. Matching ignores case and accents; a word that starts a word of the text is preferred over a hit inside one.
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> Highlights(string text, IReadOnlyList<string> words)
    {
        if (text.Length == 0 || words.Count == 0)
        {
            return Array.Empty<(int, int)>();
        }

        // Fold char by char so every folded char maps back to its original index (folding can drop accent marks).
        var folded = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var piece = Fold(text[i].ToString());
            foreach (var c in piece)
            {
                folded.Append(c);
                map.Add(i);
            }
        }

        var f = folded.ToString();
        var ranges = new List<(int Start, int Length)>();
        foreach (var word in words)
        {
            var at = f.IndexOf(word, StringComparison.Ordinal);
            var first = at;
            while (at > 0 && char.IsLetterOrDigit(f[at - 1]))
            {
                at = f.IndexOf(word, at + 1, StringComparison.Ordinal);
            }

            if (at < 0)
            {
                at = first;
            }

            if (at >= 0)
            {
                var start = map[at];
                var end = map[at + word.Length - 1] + 1;
                ranges.Add((start, end - start));
            }
        }

        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<(int Start, int Length)>();
        foreach (var r in ranges)
        {
            if (merged.Count > 0 && r.Start <= merged[^1].Start + merged[^1].Length)
            {
                var last = merged[^1];
                merged[^1] = (last.Start, Math.Max(last.Start + last.Length, r.Start + r.Length) - last.Start);
            }
            else
            {
                merged.Add(r);
            }
        }

        return merged;
    }

    /// <summary>Splits folded text into words on anything that is not a letter or digit.</summary>
    public static string[] Words(string folded) =>
        folded.Split(new[] { ' ', '-', '_', '.', ',', ':', ';', '/', '\\', '(', ')', '[', ']', '&', '+', '\'', '"', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
}
