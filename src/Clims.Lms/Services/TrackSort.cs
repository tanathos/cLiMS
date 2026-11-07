namespace Clims.Lms;

/// <summary>Sort orders of <c>titles</c>.</summary>
public enum TrackSort
{
    /// <summary>By title.</summary>
    Title,

    /// <summary>By track number (use with an album filter).</summary>
    TrackNumber,

    /// <summary>By album, then track number.</summary>
    AlbumTrack,

    /// <summary>Newest added first.</summary>
    New,

    /// <summary>Random order.</summary>
    Random,
}
