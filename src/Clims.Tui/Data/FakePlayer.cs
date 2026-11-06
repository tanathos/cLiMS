namespace Clims.Tui.Data;

public sealed class FakePlayer : IPlayerController
{
    private AlbumItem _album;
    private int _index;

    public FakePlayer(AlbumItem start)
    {
        _album = start;
        IsPlaying = true;
        Elapsed = TimeSpan.FromSeconds(40);
    }

    public string PlayerName => "piCorePlayer";

    public bool IsPlaying { get; private set; }

    public int Volume { get; private set; } = 70;

    public TrackItem? Current => _album.Tracks[_index];

    public AlbumItem? CurrentAlbum => _album;

    public TimeSpan Elapsed { get; private set; }

    public event Action? Changed;

    public void TogglePlay()
    {
        IsPlaying = !IsPlaying;
        Changed?.Invoke();
    }

    public void Next() => Go(_index + 1);

    public void Previous() => Go(Elapsed > TimeSpan.FromSeconds(3) ? _index : _index - 1);

    public void Seek(TimeSpan delta)
    {
        var d = Current!.Duration;
        Elapsed = TimeSpan.FromSeconds(Math.Clamp((Elapsed + delta).TotalSeconds, 0, d.TotalSeconds - 0.1));
        Changed?.Invoke();
    }

    public void SeekTo(double fraction)
    {
        Elapsed = TimeSpan.FromSeconds(Math.Clamp(fraction, 0, 1) * Current!.Duration.TotalSeconds);
        Changed?.Invoke();
    }

    public void SetVolume(int volume)
    {
        Volume = Math.Clamp(volume, 0, 100);
        Changed?.Invoke();
    }

    public void Play(AlbumItem album, int trackIndex)
    {
        _album = album;
        IsPlaying = true;
        Go(trackIndex);
    }

    public void Tick(TimeSpan dt)
    {
        if (!IsPlaying)
        {
            return;
        }

        Elapsed += dt;
        if (Elapsed >= Current!.Duration)
        {
            Go(_index + 1);
        }
    }

    private void Go(int index)
    {
        _index = (index + _album.Tracks.Count) % _album.Tracks.Count;
        Elapsed = TimeSpan.Zero;
        Changed?.Invoke();
    }
}
