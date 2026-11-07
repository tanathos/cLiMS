namespace Clims.Lms;

/// <summary>Filters for track listings. At least one filter should be set: the library has tens of thousands of tracks.</summary>
public sealed record TrackQuery
{
    /// <summary>Only tracks of this album (overrides other filters on the server).</summary>
    public int? AlbumId { get; init; }

    /// <summary>Only tracks of this artist.</summary>
    public int? ArtistId { get; init; }

    /// <summary>Only tracks with this genre.</summary>
    public int? GenreId { get; init; }

    /// <summary>Word-prefix search text.</summary>
    public string? Search { get; init; }

    /// <summary>Sort order.</summary>
    public TrackSort Sort { get; init; } = TrackSort.AlbumTrack;

    /// <summary>Fields to request.</summary>
    public TrackTags Tags { get; init; } = TrackTags.List;
}
