using System.Net;
using System.Text.RegularExpressions;

namespace Clims.Lyrics;

/// <summary>Reads a DarkLyrics album page: one <c>&lt;h3&gt;&lt;a name="N"&gt;N. Title&lt;/a&gt;&lt;/h3&gt;</c> per song, its words after it, lines ending in <c>&lt;br /&gt;</c>.</summary>
public static partial class DarkLyricsParser
{
    [GeneratedRegex(@"<h3><a name=""(\d+)"">(.*?)</a></h3>", RegexOptions.Singleline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"<div class=""(thanks|note)"">|</div>", RegexOptions.IgnoreCase)]
    private static partial Regex End();

    [GeneratedRegex(@"^\s*(<i>\s*\[[^<\]]*\]\s*</i>\s*(<br\s*/?>)?\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex Credits();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex Break();

    [GeneratedRegex(@"^\s*(<br\s*/?>\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingBreaks();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"^\s*\d+\s*\.\s*")]
    private static partial Regex Numbering();

    /// <summary>The songs of the page, in order: track number, title and words. Empty when the page is not an album page.</summary>
    public static IReadOnlyList<(int Number, string Title, string Text)> Parse(string html)
    {
        var start = html.IndexOf("<div class=\"lyrics\">", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return [];
        }

        var page = html[start..];
        var headings = Heading().Matches(page);
        var songs = new List<(int, string, string)>();
        for (var i = 0; i < headings.Count; i++)
        {
            var h = headings[i];
            var from = h.Index + h.Length;
            var to = i + 1 < headings.Count ? headings[i + 1].Index : page.Length;
            var body = page[from..to];
            if (End().Match(body) is { Success: true } end)
            {
                body = body[..end.Index];
            }

            // Newlines in the source are only layout: the lines end at the <br />.
            body = Credits().Replace(LeadingBreaks().Replace(body.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal), ""), "");
            var text = WebUtility.HtmlDecode(Tag().Replace(Break().Replace(body, "\n"), "")).Replace("\r", "", StringComparison.Ordinal);
            var lines = text.Split('\n').Select(l => l.TrimEnd()).ToList();
            var title = WebUtility.HtmlDecode(Tag().Replace(h.Groups[2].Value, ""));
            songs.Add((int.Parse(h.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), Numbering().Replace(title, "").Trim(), string.Join('\n', lines).Trim()));
        }

        return songs;
    }
}
