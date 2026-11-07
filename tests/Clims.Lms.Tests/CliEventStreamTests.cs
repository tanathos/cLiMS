using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Clims.Lms.Tests;

public class CliEventStreamTests
{
    private const string EncodedMac = "b8%3A27%3Aeb%3Aa9%3A77%3A8f";

    private static CliEventStream Stream(int port) => new(new CliEventStreamOptions
    {
        Host = "127.0.0.1",
        Port = port,
        MinReconnectDelay = TimeSpan.FromMilliseconds(30),
        MaxReconnectDelay = TimeSpan.FromMilliseconds(60),
        DedupeWindow = TimeSpan.FromSeconds(2),
    });

    private static async Task<List<LmsEvent>> Collect(CliEventStream stream, Func<LmsEvent, bool> done, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        var events = new List<LmsEvent>();
        try
        {
            await foreach (var e in stream.ReadAllAsync(cts.Token))
            {
                events.Add(e);
                if (done(e))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("timed out waiting for events; got: " + string.Join(", ", events.Select(e => e.GetType().Name)));
        }

        return events;
    }

    [Fact]
    public async Task Subscribes_with_listen_1_and_delivers_parsed_deduplicated_events()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        string? firstLine = null;

        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            firstLine = await reader.ReadLineAsync();
            var lines = new[]
            {
                "listen 1",                                                              // echo of our command: noise
                $"{EncodedMac} material-skin client-registration",                       // noise
                $"{EncodedMac} playlist newsong Satellites 3",
                $"{EncodedMac} playlist newsong Satellites 3",                           // duplicate
                $"{EncodedMac} mixer volume 40",
                $"{EncodedMac} prefset server volume 40",                                // noise
                "rescan done",
                "rescan done",                                                           // fires twice, even with no changes
            };
            var bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
            await stream.WriteAsync(bytes);
            await Task.Delay(TimeSpan.FromSeconds(3));
        });

        try
        {
            var events = await Collect(Stream(port), e => e is ScanFinishedEvent);

            Assert.Equal("listen 1", firstLine);
            Assert.Collection(
                events,
                e => Assert.True(Assert.IsType<EventStreamStateEvent>(e).Connected),
                e => Assert.Equal("Satellites", Assert.IsType<TrackChangedEvent>(e).Title),
                e => Assert.Equal(40, Assert.IsType<VolumeChangedEvent>(e).Volume),
                e => Assert.IsType<ScanFinishedEvent>(e));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Reconnects_after_the_server_drops_the_connection()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var server = Task.Run(async () =>
        {
            for (var session = 0; session < 2; session++)
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.UTF8);
                await reader.ReadLineAsync();
                var line = session == 0 ? $"{EncodedMac} play\n" : $"{EncodedMac} stop\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(line));
                if (session == 0)
                {
                    await Task.Delay(100); // then hang up
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
        });

        try
        {
            var events = await Collect(Stream(port), e => e is PlaybackStateEvent { Mode: PlayMode.Stop });

            var kinds = events.Select(e => e switch
            {
                EventStreamStateEvent s => s.Connected ? "up" : "down",
                PlaybackStateEvent p => p.Mode.ToString()!,
                _ => e.GetType().Name,
            });
            Assert.Equal(new[] { "up", "Play", "down", "up", "Stop" }, kinds);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Reports_offline_and_keeps_retrying_while_nothing_listens()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var events = await Collect(Stream(port), e => e is EventStreamStateEvent { Connected: false }, TimeSpan.FromSeconds(10));
        var down = Assert.IsType<EventStreamStateEvent>(Assert.Single(events));
        Assert.False(down.Connected);
        Assert.False(string.IsNullOrEmpty(down.Reason));
    }

    [Fact]
    public async Task Stops_cleanly_when_the_consumer_cancels()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await Task.Delay(TimeSpan.FromSeconds(5));
        });

        try
        {
            using var cts = new CancellationTokenSource();
            var seen = new List<LmsEvent>();
            var run = Task.Run(async () =>
            {
                await foreach (var e in Stream(port).ReadAllAsync(cts.Token))
                {
                    seen.Add(e);
                    cts.Cancel();
                }
            });

            var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(run, finished);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            Assert.NotEmpty(seen);
        }
        finally
        {
            listener.Stop();
        }
    }
}
