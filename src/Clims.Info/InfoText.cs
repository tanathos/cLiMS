using System.Globalization;
using System.Text;

namespace Clims.Info;

/// <summary>Name matching shared by the providers.</summary>
internal static class InfoText
{
    /// <summary>A name reduced to its letters and digits, lower case, without accents: "Motörhead" and "MOTORHEAD!" match.</summary>
    public static string Normalize(string? text)
    {
        var builder = new StringBuilder();
        foreach (var c in (text ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    /// <summary>True for the placeholder artists that no database has one page for.</summary>
    public static bool IsVariousArtists(string? artist)
    {
        var n = Normalize(artist);
        return n is "" or "variousartists" or "various" or "va" or "unknown" or "unknownartist";
    }

    /// <summary>The text made safe inside a quoted phrase of a Lucene query.</summary>
    public static string Lucene(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
