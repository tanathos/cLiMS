namespace Clims.Catalog;

internal static class TextKeys
{
    public static string For(string name)
    {
        var s = name.AsSpan().TrimStart();
        if (s.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
        {
            s = s[4..].TrimStart();
        }

        if (s.IsEmpty)
        {
            return "#";
        }

        var folded = TextFolding.Fold(s[..1].ToString());
        return folded.Length > 0 && char.IsLetter(folded[0]) ? char.ToUpperInvariant(folded[0]).ToString() : "#";
    }
}
