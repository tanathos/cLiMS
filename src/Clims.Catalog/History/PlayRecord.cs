namespace Clims.Catalog;

/// <summary>
/// One play of a track, as written to the history. The names are copied in, so the entry still reads well if the library is rescanned
/// and the server hands out new ids.
/// </summary>
/// <param name="TrackId">LMS track id at the time.</param>
/// <param name="AlbumId">LMS album id at the time.</param>
/// <param name="PlayedAt">When the track counted as played.</param>
/// <param name="Artist">Album artist (what the album is listed under).</param>
/// <param name="Album">Album title.</param>
/// <param name="Title">Track title.</param>
/// <param name="TrackNumber">Track number on its disc, 0 when unknown.</param>
/// <param name="AlbumTracks">How many tracks the album had: the "half an album" rule needs it.</param>
public sealed record PlayRecord(
    int TrackId,
    int AlbumId,
    DateTimeOffset PlayedAt,
    string Artist,
    string Album,
    string Title,
    int TrackNumber,
    int AlbumTracks);
