namespace Clims.Lyrics;

/// <summary>What is known about the track whose lyrics are wanted.</summary>
/// <param name="Artist">Track artist (the album artist when the track has none).</param>
/// <param name="Album">Album title.</param>
/// <param name="Title">Track title.</param>
/// <param name="Duration">Length, used to tell versions apart (zero: unknown).</param>
/// <param name="AlbumArtist">Album artist, for providers that file lyrics by album.</param>
/// <param name="Number">Track number on its disc (0: unknown).</param>
/// <param name="TrackId">LMS track id, for the sources that ask the server about this very track (null: unknown).</param>
/// <param name="Url">The track's file address as the server knows it (<c>file:///mnt/Music/...</c>), for the sources that look next to the file (null: unknown).</param>
public sealed record LyricsQuery(string Artist, string Album, string Title, TimeSpan Duration, string? AlbumArtist = null, int Number = 0, int? TrackId = null, string? Url = null)
{
    /// <summary>The store key of this track.</summary>
    public string Key => LyricsKey.Of(this);
}
