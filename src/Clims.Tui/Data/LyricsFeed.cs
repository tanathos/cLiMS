using Clims.Lyrics;
using Clims.Tui.Ui;

namespace Clims.Tui.Data;

/// <summary>
/// The lyrics of the track being shown: asks the <see cref="LyricsService"/> (store first, then the providers, in the
/// background) and keeps the answer for the album view. A new track cancels the search of the previous one.
/// </summary>
public sealed class LyricsFeed : IDisposable
{
    private readonly Action? _enableOnline;
    private LyricsService _service;
    private NowPlaying? _now;
    private string? _key;
    private CancellationTokenSource? _cts;

    /// <summary>Creates the feed.</summary>
    /// <param name="service">What finds the lyrics.</param>
    /// <param name="enableOnline">Called when the user asks to search online; it turns the setting on and hands a new service to <see cref="Replace"/>. Null: nothing to offer.</param>
    public LyricsFeed(LyricsService service, Action? enableOnline = null)
    {
        _service = service;
        _enableOnline = enableOnline;
    }

    /// <summary>Where the lyrics of the current track stand.</summary>
    public LyricsState State { get; private set; } = LyricsState.Idle;

    /// <summary>The lyrics, when <see cref="State"/> is <see cref="LyricsState.Found"/> (kept while a refresh runs).</summary>
    public LyricsResult? Result { get; private set; }

    /// <summary>True when the user can still turn online lookup on.</summary>
    public bool CanEnableOnline => !_service.CanSearchOnline && _enableOnline is not null;

    /// <summary>Raised when <see cref="State"/> or <see cref="Result"/> changed (any thread).</summary>
    public event Action? Changed;

    /// <summary>The query for a track: the track artist (the album artist when it has none), its album, title and length.</summary>
    public static LyricsQuery QueryOf(NowPlaying now)
    {
        var artist = now.Track.Artist is { Length: > 0 } a ? a : now.Album.ArtistName;
        return new LyricsQuery(artist, now.Album.Title, now.Track.Title, now.Track.Duration, now.Album.ArtistName, now.Track.Number, now.Track.Id, now.Track.Url);
    }

    /// <summary>Shows the lyrics of a track. The same track again does nothing, unless <paramref name="refresh"/> asks for a new search.</summary>
    public void Show(NowPlaying? now, bool refresh = false)
    {
        _now = now;
        if (now is null)
        {
            Cancel();
            _key = null;
            Set(LyricsState.Idle, null);
            return;
        }

        var query = QueryOf(now);
        if (!refresh && query.Key == _key)
        {
            return;
        }

        Cancel();
        _key = query.Key;
        var stored = refresh ? null : _service.Stored(query);
        if (stored is not null)
        {
            Set(LyricsState.Found, stored);
            return;
        }

        if (!_service.CanSearch)
        {
            Set(refresh ? State : LyricsState.Off, refresh ? Result : null);
            return;
        }

        var cts = _cts = new CancellationTokenSource();
        Set(LyricsState.Loading, refresh ? Result : null);
        var service = _service;
        _ = Task.Run(async () =>
        {
            try
            {
                var found = await service.FindAsync(query, refresh, cts.Token).ConfigureAwait(false);
                if (!cts.IsCancellationRequested)
                {
                    // With only local sources "not found" is not the end: online lookup may still be turned on.
                    Set(found is not null ? LyricsState.Found : service.CanSearchOnline ? LyricsState.NotFound : LyricsState.Off, found);
                }
            }
            catch (OperationCanceledException)
            {
                // The track changed.
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                if (!cts.IsCancellationRequested)
                {
                    Set(LyricsState.NotFound, null);
                }
            }
        });
    }

    /// <summary>Turns online lookup on (when offered) and searches for the current track.</summary>
    public void EnableOnline()
    {
        if (CanEnableOnline)
        {
            _enableOnline!();
        }
    }

    /// <summary>Swaps the service (settings changed) and looks the current track up again.</summary>
    public void Replace(LyricsService service)
    {
        _service = service;
        _key = null;
        Show(_now);
    }

    /// <summary>Stops a running search.</summary>
    public void Dispose() => Cancel();

    private void Cancel()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private void Set(LyricsState state, LyricsResult? result)
    {
        State = state;
        Result = result;
        Changed?.Invoke();
    }
}
