namespace Clims.Tui.Data;

public sealed record ArtistItem(int Id, string Name, int AlbumCount);

public sealed record TrackItem(int Id, int Number, string Title, string Artist, string Album, TimeSpan Duration);

public sealed record AlbumItem(
    int Id,
    string Title,
    string Artist,
    int Year,
    string Genres,
    TimeSpan Duration,
    int ArtSeed,
    IReadOnlyList<TrackItem> Tracks);

public sealed record SearchResults(
    IReadOnlyList<ArtistItem> Artists,
    IReadOnlyList<AlbumItem> Albums,
    IReadOnlyList<TrackItem> Tracks);

public enum SyncState
{
    NotSynced,
    InProgress,
    Synced,
}
