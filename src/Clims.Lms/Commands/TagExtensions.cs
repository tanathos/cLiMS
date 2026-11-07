namespace Clims.Lms;

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
