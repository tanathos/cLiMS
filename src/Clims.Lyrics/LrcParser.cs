using System.Globalization;
using System.Text.RegularExpressions;

namespace Clims.Lyrics;

/// <summary>Reads LRC text: <c>[mm:ss.xx]words</c>, several stamps on one line, the <c>[offset:ms]</c> tag, and ignores other tags.</summary>
public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex Stamp();

    [GeneratedRegex(@"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex Offset();

    [GeneratedRegex(@"<\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?>")]
    private static partial Regex WordStamp();

    /// <summary>The lines in time order. Text without any stamp is skipped.</summary>
    public static IReadOnlyList<LyricLine> Parse(string lrc)
    {
        var offset = TimeSpan.Zero;
        if (Offset().Match(lrc) is { Success: true } o)
        {
            // A positive offset makes the lyrics appear sooner (LRC convention), so it is taken off the stamps.
            offset = TimeSpan.FromMilliseconds(long.Parse(o.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        var lines = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var text = raw.TrimEnd('\r');
            var matches = Stamp().Matches(text);
            if (matches.Count == 0)
            {
                continue;
            }

            // Everything after the last stamp is the words (enhanced LRC's per-word <stamps> are dropped).
            var last = matches[^1];
            var words = WordStamp().Replace(text[(last.Index + last.Length)..], "").Trim();
            foreach (Match m in matches)
            {
                var fraction = m.Groups[3].Success ? double.Parse("0." + m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
                var seconds = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) + fraction;
                var time = TimeSpan.FromSeconds(seconds) - offset;
                lines.Add(new LyricLine(time < TimeSpan.Zero ? TimeSpan.Zero : time, words));
            }
        }

        return lines.OrderBy(l => l.Time).ToList();
    }

    /// <summary>Index of the line being sung at <paramref name="elapsed"/>, or -1 before the first line.</summary>
    public static int IndexAt(IReadOnlyList<LyricLine> lines, TimeSpan elapsed)
    {
        var lo = 0;
        var hi = lines.Count - 1;
        var found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (lines[mid].Time <= elapsed)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found;
    }
}
