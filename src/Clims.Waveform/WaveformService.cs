using Clims.Lms;

namespace Clims.Waveform;

/// <summary>Where a track's waveform stands.</summary>
public enum WaveformState
{
    /// <summary>Nobody asked for it yet.</summary>
    Unknown,

    /// <summary>Downloading or decoding.</summary>
    Loading,

    /// <summary>Peaks are available.</summary>
    Ready,

    /// <summary>The format cannot be decoded (shown as "no waveform", never retried).</summary>
    Unsupported,

    /// <summary>Download or decode failed (retried after a pause).</summary>
    Failed,
}

/// <summary>Settings for <see cref="WaveformService"/>.</summary>
public sealed class WaveformOptions
{
    /// <summary>Peaks per track.</summary>
    public int Points { get; init; } = PeakAnalyzer.DefaultPoints;

    /// <summary>Profiles kept in memory (about 5 KB each).</summary>
    public int CacheSize { get; init; } = 64;

    /// <summary>Largest file that is downloaded for analysis.</summary>
    public long MaxBytes { get; init; } = 400L * 1024 * 1024;

    /// <summary>How long a failed track is left alone before it can be tried again.</summary>
    public TimeSpan RetryAfter { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Folder for the temporary download; the system temp folder by default.</summary>
    public string? TempFolder { get; init; }

    /// <summary>Clock, replaceable in tests.</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;
}

/// <summary>
/// Overview waveforms for the player: downloads the original file of the current track (and the next one, ahead of time),
/// decodes it and keeps the peaks in memory. One download at a time, the current track always first, work for tracks
/// nobody wants any more is cancelled. Read-only on the server (a plain HTTP download, no effect on playback).
/// </summary>
public sealed class WaveformService : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly WaveformOptions _options;
    private readonly object _gate = new();
    private readonly Dictionary<int, PeakProfile> _ready = new();
    private readonly LinkedList<int> _recent = new();
    private readonly Dictionary<int, WaveformState> _states = new();
    private readonly Dictionary<int, DateTimeOffset> _failedAt = new();
    private readonly Dictionary<int, string> _errors = new();
    private int[] _wanted = Array.Empty<int>();
    private CancellationTokenSource? _runCts;
    private int _runningTrack = -1;
    private bool _pumping;
    private bool _disposed;

    /// <summary>Creates the service. <paramref name="http"/> stays owned by the caller.</summary>
    public WaveformService(HttpClient http, Uri baseUri, WaveformOptions? options = null)
    {
        _http = http;
        _baseUri = baseUri;
        _options = options ?? new WaveformOptions();
    }

    /// <summary>Raised (any thread) when a profile became ready or a track failed.</summary>
    public event Action? Updated;

    /// <summary>The profile of a track, when ready.</summary>
    public PeakProfile? TryGet(int trackId)
    {
        lock (_gate)
        {
            return _ready.TryGetValue(trackId, out var p) ? p : null;
        }
    }

    /// <summary>Why a track is <see cref="WaveformState.Failed"/> or <see cref="WaveformState.Unsupported"/>; null otherwise.</summary>
    public string? ErrorOf(int trackId)
    {
        lock (_gate)
        {
            return _errors.GetValueOrDefault(trackId);
        }
    }

    /// <summary>Where a track stands.</summary>
    public WaveformState StateOf(int trackId)
    {
        lock (_gate)
        {
            return _states.GetValueOrDefault(trackId, WaveformState.Unknown);
        }
    }

    /// <summary>
    /// Says which tracks matter now, most important first (the current track, then the next one). Starts loading those
    /// that are not ready and cancels a running download for a track that is no longer in the list.
    /// </summary>
    public void Want(params int[] trackIds)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _wanted = trackIds;
            if (_runningTrack >= 0 && !trackIds.Contains(_runningTrack))
            {
                _runCts?.Cancel();
            }

            if (_pumping)
            {
                return;
            }

            _pumping = true;
        }

        _ = Task.Run(PumpAsync);
    }

    /// <summary>Cancels everything.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _runCts?.Cancel();
        }
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            int next;
            CancellationTokenSource cts;
            lock (_gate)
            {
                next = _wanted.FirstOrDefault(id => NeedsLoad(id), -1);
                if (next < 0 || _disposed)
                {
                    _pumping = false;
                    _runningTrack = -1;
                    return;
                }

                _runningTrack = next;
                _states[next] = WaveformState.Loading;
                cts = _runCts = new CancellationTokenSource();
            }

            Updated?.Invoke();
            try
            {
                var profile = await LoadAsync(next, cts.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    Store(next, profile);
                }
            }
            catch (OperationCanceledException)
            {
                lock (_gate)
                {
                    _states[next] = WaveformState.Unknown; // not wanted any more, or shutting down: may be asked again later
                }
            }
            catch (UnsupportedAudioException ex)
            {
                lock (_gate)
                {
                    _states[next] = WaveformState.Unsupported;
                    _errors[next] = ex.Message;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or NotSupportedException or FormatException)
            {
                lock (_gate)
                {
                    _states[next] = WaveformState.Failed;
                    _errors[next] = ex.Message;
                    _failedAt[next] = _options.Now();
                }
            }
            finally
            {
                // Nobody may cancel it after it is disposed: forget it under the lock first.
                lock (_gate)
                {
                    _runCts = null;
                    _runningTrack = -1;
                }

                cts.Dispose();
            }

            Updated?.Invoke();
        }
    }

    private bool NeedsLoad(int id)
    {
        switch (_states.GetValueOrDefault(id, WaveformState.Unknown))
        {
            case WaveformState.Ready:
            case WaveformState.Unsupported:
                return false;
            case WaveformState.Failed:
                return _options.Now() - _failedAt.GetValueOrDefault(id) >= _options.RetryAfter;
            default:
                return true;
        }
    }

    private void Store(int id, PeakProfile profile)
    {
        _ready[id] = profile;
        _states[id] = WaveformState.Ready;
        _recent.Remove(id);
        _recent.AddFirst(id);
        while (_recent.Count > _options.CacheSize)
        {
            var oldest = _recent.Last!.Value;
            _recent.RemoveLast();
            _ready.Remove(oldest);
            _states.Remove(oldest);
        }
    }

    private async Task<PeakProfile> LoadAsync(int trackId, CancellationToken ct)
    {
        var folder = _options.TempFolder ?? Path.GetTempPath();
        var path = Path.Combine(folder, $"clims-wave-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var response = await _http.GetAsync(AudioUrl.Download(_baseUri, trackId), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is { } length && length > _options.MaxBytes)
                {
                    throw new UnsupportedAudioException("The file is too large to analyse.");
                }

                await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await body.CopyToAsync(file, ct).ConfigureAwait(false);
            }

            return await Task.Run(
                () =>
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return PeakAnalyzer.Analyze(stream, _options.Points, ct);
                },
                ct).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Left in the temp folder; the system cleans it.
            }
        }
    }
}
