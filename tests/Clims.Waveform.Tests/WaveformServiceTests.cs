using System.Net;
using Clims.Lms;
using NAudio.Wave;

namespace Clims.Waveform.Tests;

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
