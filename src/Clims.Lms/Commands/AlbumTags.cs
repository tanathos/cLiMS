namespace Clims.Lms;

/// <summary>Fields to request for <c>albums</c>. Not the same letters as <see cref="TrackTags"/>.</summary>
[Flags]
public enum AlbumTags
{
    /// <summary>No extra fields (id and title are always present).</summary>
    None = 0,

    /// <summary>Album name (<c>l</c>).</summary>
    Album = 1 << 0,

    /// <summary>Year (<c>y</c>).</summary>
    Year = 1 << 1,

    /// <summary>Artwork track id for cover URLs (<c>j</c>).</summary>
    ArtworkTrackId = 1 << 2,

    /// <summary>Disc number (<c>i</c>).</summary>
    Disc = 1 << 3,

    /// <summary>Disc count (<c>q</c>).</summary>
    DiscCount = 1 << 4,

    /// <summary>Compilation flag (<c>w</c>).</summary>
    Compilation = 1 << 5,

    /// <summary>Release type (<c>W</c>).</summary>
    ReleaseType = 1 << 6,

    /// <summary>Album artist name (<c>a</c>).</summary>
    Artist = 1 << 7,

    /// <summary>Album artist id (<c>S</c>).</summary>
    ArtistId = 1 << 8,

    /// <summary>First-letter key for A-Z jumping (<c>s</c>).</summary>
    TextKey = 1 << 9,

    /// <summary>What an album list needs.</summary>
    List = Album | Year | ArtworkTrackId | Compilation | ReleaseType | Artist | ArtistId | TextKey | DiscCount,
}
