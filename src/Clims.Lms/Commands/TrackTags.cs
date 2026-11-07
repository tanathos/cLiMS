namespace Clims.Lms;

/// <summary>Fields to request for tracks (<c>titles</c>, <c>status</c>, <c>songinfo</c>); each maps to one tag letter. Replies are large, so ask for little.</summary>
[Flags]
public enum TrackTags
{
    /// <summary>No extra fields (id and title are always present).</summary>
    None = 0,

    /// <summary>Artist name (<c>a</c>).</summary>
    Artist = 1 << 0,

    /// <summary>Album title (<c>l</c>).</summary>
    Album = 1 << 1,

    /// <summary>Duration (<c>d</c>).</summary>
    Duration = 1 << 2,

    /// <summary>Track number (<c>t</c>).</summary>
    TrackNumber = 1 << 3,

    /// <summary>Year (<c>y</c>).</summary>
    Year = 1 << 4,

    /// <summary>File URL, the durable key (<c>u</c>).</summary>
    Url = 1 << 5,

    /// <summary>Artwork track id for cover URLs (<c>J</c>).</summary>
    ArtworkTrackId = 1 << 6,

    /// <summary>Cover id (<c>c</c>).</summary>
    CoverId = 1 << 7,

    /// <summary>Album id (<c>e</c>).</summary>
    AlbumId = 1 << 8,

    /// <summary>Artist id (<c>s</c>).</summary>
    ArtistId = 1 << 9,

    /// <summary>Genre id (<c>p</c>).</summary>
    GenreId = 1 << 10,

    /// <summary>Genre name (<c>g</c>).</summary>
    Genre = 1 << 11,

    /// <summary>Disc number (<c>i</c>).</summary>
    Disc = 1 << 12,

    /// <summary>Disc count (<c>q</c>).</summary>
    DiscCount = 1 << 13,

    /// <summary>Bitrate (<c>r</c>).</summary>
    Bitrate = 1 << 14,

    /// <summary>Sample rate (<c>T</c>).</summary>
    SampleRate = 1 << 15,

    /// <summary>Sample size (<c>I</c>).</summary>
    SampleSize = 1 << 16,

    /// <summary>Lossless flag (<c>Q</c>).</summary>
    Lossless = 1 << 17,

    /// <summary>Compilation flag (<c>C</c>).</summary>
    Compilation = 1 << 18,

    /// <summary>What a track list needs: artist, album, duration, number, ids, artwork and file URL.</summary>
    List = Artist | Album | Duration | TrackNumber | Year | Url | ArtworkTrackId | AlbumId | ArtistId | Disc,

    /// <summary>Everything the models understand.</summary>
    All = (1 << 19) - 1,
}
