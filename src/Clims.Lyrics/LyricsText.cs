namespace Clims.Lyrics;

/// <summary>Turns text found in a tag or a file into lyrics: LRC with timestamps, a plain lyric, an "instrumental" note, or nothing usable.</summary>
internal static class LyricsText
{
    /// <summary>
    /// The lyrics in <paramref name="raw"/>, or null when it is not worth showing (empty, a few characters such as
    /// "XXX|" or a placeholder): the next source then gets its chance.
    /// </summary>
    public static LyricsResult? From(string? raw, string source)
    {
        if (raw is null)
        {
            return null;
        }

        var text = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim('﻿').Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Trim('[', ']', '(', ')', ' ').Equals("instrumental", StringComparison.OrdinalIgnoreCase))
        {
            return new LyricsResult("", null, source, Instrumental: true);
        }

        var lines = LrcParser.Parse(text);
        if (lines.Count >= 2)
        {
            return new LyricsResult(string.Join('\n', lines.Select(l => l.Text)).Trim(), text, source);
        }

        return text.Length < 20 || !text.Any(char.IsLetter) ? null : new LyricsResult(text, null, source);
    }
}
