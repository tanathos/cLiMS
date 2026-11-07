namespace Clims.Lms;

/// <summary>Filters for artist listings.</summary>
public sealed record ArtistQuery
{
    /// <summary>Only artists with this genre.</summary>
    public int? GenreId { get; init; }

    /// <summary>Word-prefix search text.</summary>
    public string? Search { get; init; }
}
