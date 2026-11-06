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

/// <summary>Turns tag flags into the <c>tags:</c> command argument.</summary>
public static class TagExtensions
{
    private static readonly (TrackTags Flag, char Letter)[] TrackLetters =
    {
        (TrackTags.Artist, 'a'), (TrackTags.Album, 'l'), (TrackTags.Duration, 'd'), (TrackTags.TrackNumber, 't'),
        (TrackTags.Year, 'y'), (TrackTags.Url, 'u'), (TrackTags.ArtworkTrackId, 'J'), (TrackTags.CoverId, 'c'),
        (TrackTags.AlbumId, 'e'), (TrackTags.ArtistId, 's'), (TrackTags.GenreId, 'p'), (TrackTags.Genre, 'g'),
        (TrackTags.Disc, 'i'), (TrackTags.DiscCount, 'q'), (TrackTags.Bitrate, 'r'), (TrackTags.SampleRate, 'T'),
        (TrackTags.SampleSize, 'I'), (TrackTags.Lossless, 'Q'), (TrackTags.Compilation, 'C'),
    };

    private static readonly (AlbumTags Flag, char Letter)[] AlbumLetters =
    {
        (AlbumTags.Album, 'l'), (AlbumTags.Year, 'y'), (AlbumTags.ArtworkTrackId, 'j'), (AlbumTags.Disc, 'i'),
        (AlbumTags.DiscCount, 'q'), (AlbumTags.Compilation, 'w'), (AlbumTags.ReleaseType, 'W'),
        (AlbumTags.Artist, 'a'), (AlbumTags.ArtistId, 'S'), (AlbumTags.TextKey, 's'),
    };

    /// <summary>Returns <c>tags:</c> followed by the letters of the set flags, for example <c>tags:aldu</c>.</summary>
    public static string ToTagArgument(this TrackTags tags) =>
        "tags:" + new string(TrackLetters.Where(t => tags.HasFlag(t.Flag)).Select(t => t.Letter).ToArray());

    /// <summary>Returns <c>tags:</c> followed by the letters of the set flags.</summary>
    public static string ToTagArgument(this AlbumTags tags) =>
        "tags:" + new string(AlbumLetters.Where(t => tags.HasFlag(t.Flag)).Select(t => t.Letter).ToArray());
}
