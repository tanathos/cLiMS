using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Clims.Lyrics;

/// <summary>
/// The identity of a song in the store: artist, album and title folded to plain lower-case words, so that "Motörhead" and
/// "Motorhead", or "Song (2011 Remaster)" and "Song", are the same entry. LMS track ids are not used: a rescan changes them.
/// </summary>
public static partial class LyricsKey
{
    [GeneratedRegex(@"[\(\[][^)\]]*\b(remaster(ed)?|deluxe|edition|mono|stereo|bonus|expanded|anniversary|reissue)\b[^)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex Bracketed();

    [GeneratedRegex(@"\s+[-–]\s+(\d{4}\s+)?(remaster(ed)?|deluxe|mono|stereo|bonus|expanded|anniversary|reissue)\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex Trailing();

    /// <summary>The store key for a track.</summary>
    public static string Of(LyricsQuery q) => $"{Fold(q.Artist)}|{Fold(q.Album)}|{Fold(q.Title)}";

    /// <summary>Lower case, no accents, no punctuation, no remaster/edition tags, single spaces.</summary>
    public static string Fold(string text)
    {
        var s = Trailing().Replace(Bracketed().Replace(text, ""), "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (sb.Length > 0 && sb[^1] != ' ')
            {
                sb.Append(' ');
            }
        }

        return sb.ToString().Trim();
    }
}
