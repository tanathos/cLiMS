namespace Clims.Catalog;

/// <summary>Derived facts of an album for display.</summary>
/// <param name="TrackCount">Tracks known for the album (0 until they are loaded).</param>
/// <param name="Duration">Total length of the known tracks.</param>
/// <param name="Genres">Distinct genres of the known tracks, most frequent first.</param>
public sealed record AlbumSummary(int TrackCount, TimeSpan Duration, IReadOnlyList<string> Genres);
