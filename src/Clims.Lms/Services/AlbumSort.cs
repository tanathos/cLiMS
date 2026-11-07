namespace Clims.Lms;

/// <summary>Sort orders of <c>albums</c>.</summary>
public enum AlbumSort
{
    /// <summary>By album title.</summary>
    Album,

    /// <summary>By artist, then album title.</summary>
    ArtistAlbum,

    /// <summary>By year, then album title.</summary>
    YearAlbum,

    /// <summary>By year, artist, album.</summary>
    YearArtistAlbum,

    /// <summary>Newest added first.</summary>
    New,

    /// <summary>Random order (a different order on every call).</summary>
    Random,
}
