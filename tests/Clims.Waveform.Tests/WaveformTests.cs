using System.Net;
using Clims.Lms;
using NAudio.Wave;

namespace Clims.Waveform.Tests;

internal static class Audio
{
    /// <summary>A 16-bit WAV: <paramref name="quiet"/> seconds at low amplitude, then <paramref name="loud"/> seconds at high amplitude.</summary>
    public static byte[] Wav(double quiet, double loud, int channels = 1, int rate = 8000, double quietLevel = 0.05, double loudLevel = 0.9)
    {
        using var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(ms, new WaveFormat(rate, 16, channels)))
        {
            void Tone(double seconds, double level)
            {
                var frames = (int)(seconds * rate);
                for (var i = 0; i < frames; i++)
                {
                    var v = (short)(Math.Sin(i * 2 * Math.PI * 440 / rate) * level * short.MaxValue);
                    for (var c = 0; c < channels; c++)
                    {
                        writer.WriteByte((byte)(v & 0xff));
                        writer.WriteByte((byte)(v >> 8));
                    }
                }
            }

            Tone(quiet, quietLevel);
            Tone(loud, loudLevel);
        }

        return ms.ToArray();
    }
}

public class PeakAnalyzerTests
{
    [Fact]
    public void A_quiet_start_and_a_loud_end_show_in_the_peaks()
    {
        using var stream = new MemoryStream(Audio.Wav(1, 1));
        var p = PeakAnalyzer.Analyze(stream, points: 100);

        Assert.Equal(100, p.Peaks.Length);
        Assert.Equal(2, p.Duration.TotalSeconds, 2);
        Assert.True(p.Peaks[..45].Max() < 0.4f, "first half should be quiet");
        Assert.True(p.Peaks[55..].Min() > 0.9f, "second half should be at the top");
        Assert.Equal(1f, p.Peaks.Max(), 3);
    }

    [Fact]
    public void A_quiet_stereo_track_is_normalised()
    {
        using var stream = new MemoryStream(Audio.Wav(0.5, 0.5, channels: 2, quietLevel: 0.01, loudLevel: 0.02));
        var p = PeakAnalyzer.Analyze(stream, points: 50);
        Assert.Equal(1f, p.Peaks.Max(), 3);   // even a very quiet file reaches the top
        Assert.True(p.Peaks[0] < p.Peaks[^1]);
    }

    [Fact]
    public void Silence_gives_flat_peaks_without_dividing_by_zero()
    {
        using var stream = new MemoryStream(Audio.Wav(1, 0, quietLevel: 0));
        var p = PeakAnalyzer.Analyze(stream, points: 10);
        Assert.All(p.Peaks, v => Assert.Equal(0f, v));
    }

    [Theory]
    [InlineData("ID3 rest of an mp3 file")]
    [InlineData("abc")]
    public void Other_formats_are_reported_as_unsupported(string text)
    {
        using var stream = new MemoryStream(System.Text.Encoding.Latin1.GetBytes(text));
        Assert.Throws<UnsupportedAudioException>(() => PeakAnalyzer.Analyze(stream));
    }

    [Fact]
    public void Cancellation_stops_the_analysis()
    {
        using var stream = new MemoryStream(Audio.Wav(1, 1));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => PeakAnalyzer.Analyze(stream, 100, cts.Token));
    }

    [Fact]
    public void A_stream_longer_than_announced_does_not_overflow_the_last_slice()
    {
        var peaks = new float[4];
        long frame = 0;
        PeakAnalyzer.Accumulate(peaks, new[] { 0.1f, 0.2f, 0.3f, 0.9f, 0.5f, 0.5f }, 6, 1, ref frame, totalFrames: 3);
        Assert.Equal(6, frame);
        Assert.Equal(0.9f, peaks[3]);
    }
}

public class WaveformServiceTests
{
    private sealed class Server : HttpMessageHandler
    {
        public Dictionary<int, Func<CancellationToken, Task<HttpResponseMessage>>> Routes { get; } = new();

        public List<int> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var id = int.Parse(request.RequestUri!.Segments[^2].TrimEnd('/'));
            lock (Requests)
            {
                Requests.Add(id);
            }

            return Routes.TryGetValue(id, out var route)
                ? await route(cancellationToken)
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        public void Serve(int id, byte[] bytes) =>
            Routes[id] = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "timed out");
            await Task.Delay(5);
        }
    }

    private static string NewTemp()
    {
        var temp = Path.Combine(Path.GetTempPath(), "clims-wave-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        return temp;
    }

    private static (WaveformService Service, Server Server, string Temp) Build(Action<Server>? setup = null, Func<string, WaveformOptions>? options = null)
    {
        var server = new Server();
        setup?.Invoke(server);
        var temp = NewTemp();
        var o = options?.Invoke(temp) ?? new WaveformOptions { Points = 50, TempFolder = temp };
        return (new WaveformService(new HttpClient(server), new Uri("http://fake/"), o), server, temp);
    }

    [Fact]
    public async Task The_current_track_loads_first_then_the_next_and_both_are_cached()
    {
        var (service, server, temp) = Build(s => { s.Serve(1, Audio.Wav(0.2, 0.2)); s.Serve(2, Audio.Wav(0.2, 0.2)); });
        using var _ = service;

        service.Want(1, 2);
        await WaitAsync(() => service.StateOf(2) == WaveformState.Ready);

        Assert.Equal(new[] { 1, 2 }, server.Requests);
        Assert.Equal(50, service.TryGet(1)!.Peaks.Length);
        Assert.Empty(Directory.GetFiles(temp));        // temp downloads are deleted

        service.Want(1, 2);
        await Task.Delay(50);
        Assert.Equal(2, server.Requests.Count);        // nothing is fetched twice
    }

    [Fact]
    public async Task Unsupported_files_are_not_retried()
    {
        var (service, server, _) = Build(s => s.Serve(1, System.Text.Encoding.ASCII.GetBytes("ID3 mp3 data here")));
        using var _ = service;

        service.Want(1);
        await WaitAsync(() => service.StateOf(1) == WaveformState.Unsupported);
        service.Want(1);
        await Task.Delay(50);

        Assert.Single(server.Requests);
        Assert.Null(service.TryGet(1));
    }

    [Fact]
    public async Task A_failed_download_is_retried_only_after_the_pause()
    {
        var now = DateTimeOffset.UtcNow;
        var (service, server, _) = Build(options: temp => new WaveformOptions { Points = 50, TempFolder = temp, RetryAfter = TimeSpan.FromSeconds(30), Now = () => now });
        using var _ = service;                          // no route for track 1: the fake answers 404

        service.Want(1);
        await WaitAsync(() => service.StateOf(1) == WaveformState.Failed);
        service.Want(1);
        await Task.Delay(50);
        Assert.Single(server.Requests);                 // too soon

        now += TimeSpan.FromSeconds(31);
        server.Serve(1, Audio.Wav(0.2, 0.2));
        service.Want(1);
        await WaitAsync(() => service.StateOf(1) == WaveformState.Ready);
        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task A_running_download_is_cancelled_when_the_track_is_no_longer_wanted()
    {
        var started = new TaskCompletionSource();
        var cancelled = new TaskCompletionSource();
        var (service, _, _) = Build(s =>
        {
            s.Routes[1] = async ct =>
            {
                started.SetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    cancelled.SetResult();
                    throw;
                }

                return new HttpResponseMessage();
            };
            s.Serve(2, Audio.Wav(0.2, 0.2));
        });
        using var _ = service;

        service.Want(1);
        await started.Task;
        service.Want(2);                                // the user skipped ahead

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitAsync(() => service.StateOf(2) == WaveformState.Ready);
        Assert.NotEqual(WaveformState.Ready, service.StateOf(1));
    }

    [Fact]
    public async Task The_memory_cache_drops_the_oldest_profile()
    {
        var (service, _, _) = Build(
            s =>
            {
                foreach (var id in new[] { 1, 2, 3 })
                {
                    s.Serve(id, Audio.Wav(0.1, 0.1));
                }
            },
            temp => new WaveformOptions { Points = 20, CacheSize = 2, TempFolder = temp });
        using var _ = service;

        service.Want(1);
        await WaitAsync(() => service.StateOf(1) == WaveformState.Ready);
        service.Want(2);
        await WaitAsync(() => service.StateOf(2) == WaveformState.Ready);
        service.Want(3);
        await WaitAsync(() => service.StateOf(3) == WaveformState.Ready);

        Assert.Null(service.TryGet(1));
        Assert.NotNull(service.TryGet(2));
        Assert.NotNull(service.TryGet(3));
    }

    [Fact]
    public async Task Updates_are_announced()
    {
        var (service, _, _) = Build(s => s.Serve(1, Audio.Wav(0.1, 0.1)));
        using var _ = service;
        var updates = 0;
        service.Updated += () => Interlocked.Increment(ref updates);

        service.Want(1);
        await WaitAsync(() => service.StateOf(1) == WaveformState.Ready);
        await WaitAsync(() => Volatile.Read(ref updates) >= 2);   // loading, then ready
    }
}

/// <summary>Runs only when <c>CLIMS_LMS_URL</c> points at a real server: downloads one real track and draws it.</summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLIMS_LMS_URL")))
        {
            Skip = "Set CLIMS_LMS_URL to analyse a real track.";
        }
    }
}

public class RealTrackTests
{
    [IntegrationFact]
    public async Task A_real_flac_track_gets_a_plausible_waveform()
    {
        var baseUri = new Uri(Environment.GetEnvironmentVariable("CLIMS_LMS_URL")!);
        using var http = new HttpClient();
        using var lms = new LmsClient(new LmsClientOptions { BaseUri = baseUri }, http);
        var track = (await lms.Library.GetTracksAsync(new TrackQuery { Search = "iron maiden", Tags = TrackTags.Duration }, 0, 1)).Items[0];

        using var service = new WaveformService(http, baseUri);
        service.Want(track.Id);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (service.StateOf(track.Id) is WaveformState.Unknown or WaveformState.Loading)
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(120), "timed out");
            await Task.Delay(100);
        }

        Assert.True(service.StateOf(track.Id) == WaveformState.Ready, $"{service.StateOf(track.Id)}: {service.ErrorOf(track.Id)} (track {track.Id})");
        var p = service.TryGet(track.Id)!;
        Assert.Equal(PeakAnalyzer.DefaultPoints, p.Peaks.Length);
        Assert.True(Math.Abs(p.Duration.TotalSeconds - track.Duration.TotalSeconds) < 2, $"{p.Duration} vs {track.Duration}");
        Assert.True(p.Peaks.Count(v => v > 0.05f) > p.Peaks.Length / 2, "mostly not silent");
        File.WriteAllText(
            Path.Combine(Path.GetTempPath(), "clims-real-peaks.txt"),
            $"{track.Title} {p.Duration} in {sw.Elapsed.TotalSeconds:F1}s: " + string.Join(' ', p.Peaks.Where((_, i) => i % 60 == 0).Select(v => v.ToString("F2"))));
    }
}
