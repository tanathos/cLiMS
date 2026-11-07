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
