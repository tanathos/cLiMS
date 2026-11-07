namespace Clims.Lms;

/// <summary>Filters for album listings.</summary>
public sealed record AlbumQuery
{
    /// <summary>Only albums of this artist.</summary>
    public int? ArtistId { get; init; }

    /// <summary>Only albums with this genre.</summary>
    public int? GenreId { get; init; }

    /// <summary>Only albums from this year.</summary>
    public int? Year { get; init; }

    /// <summary>Word-prefix search text.</summary>
    public string? Search { get; init; }

    /// <summary>Restrict to compilations (true) or exclude them (false).</summary>
    public bool? Compilation { get; init; }

    /// <summary>Sort order.</summary>
    public AlbumSort Sort { get; init; } = AlbumSort.Album;

    /// <summary>Fields to request.</summary>
    public AlbumTags Tags { get; init; } = AlbumTags.List;
}
