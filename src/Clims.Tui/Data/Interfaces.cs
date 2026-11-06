namespace Clims.Tui.Data;

/// <summary>Read side of the catalogue. Replaced by the SQLite-backed index in Step 3.</summary>
public interface ILibrarySource
{
    IReadOnlyList<ArtistItem> Artists { get; }

    /// <summary>Albums of an artist, or every album for artist id 0 ("All Artists").</summary>
    IReadOnlyList<AlbumItem> GetAlbums(int artistId);

    AlbumItem? GetAlbumOfTrack(int trackId);

    ArtistItem? GetArtist(string name);

    SearchResults Search(string text);
}

public interface IPlayerController
{
    string PlayerName { get; }

    bool IsPlaying { get; }

    int Volume { get; }

    TrackItem? Current { get; }

    AlbumItem? CurrentAlbum { get; }

    TimeSpan Elapsed { get; }

    /// <summary>Raised when state changes; the UI marshals to its own thread.</summary>
    event Action? Changed;

    void TogglePlay();

    void Next();

    void Previous();

    void Seek(TimeSpan delta);

    void SeekTo(double fraction);

    void SetVolume(int volume);

    void Play(AlbumItem album, int trackIndex);

    /// <summary>Advances the fake clock; real implementations ignore it.</summary>
    void Tick(TimeSpan dt);
}
