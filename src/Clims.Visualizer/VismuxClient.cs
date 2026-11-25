using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Clims.Visualizer;

/// <summary>
/// Subscribes to the vismux source on the player and keeps the latest spectrum bands. Runs on its own thread, retries forever
/// (the player may be off, or vismux not started yet) and never throws to the caller.
/// </summary>
public sealed class VismuxClient : IVisualSource, IDisposable
{
    private const int FramesPerSecond = 30;
    private static readonly TimeSpan Fresh = TimeSpan.FromMilliseconds(1500);

    private readonly string _host;
    private readonly int _port;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _run;
    private readonly Action<string>? _log;
    private readonly object _gate = new();
    private readonly float[] _pending = new float[LiveSpectrum.Bands];
    private readonly float[] _pendingChroma = new float[Chromagram.Notes];
    private float _pendingBeat;
    private long _lastFrameTicks;
    private volatile bool _lastRunning;
    private volatile bool _heard;

    /// <param name="host">Address or name of the player running vismux.</param>
    /// <param name="port">The vismux port (23483 unless started with <c>--port</c>).</param>
    /// <param name="log">Told what happens (subscribed, first samples, errors), since the client otherwise fails silently.</param>
    public VismuxClient(string host, int port = VismuxProtocol.DefaultPort, Action<string>? log = null)
    {
        _host = host;
        _port = port;
        _log = log;
        _log?.Invoke($"Visualizer: subscribing to {host}:{port}");
        _run = Task.Run(() => RunAsync(_stop.Token));
    }

    public int Bands => LiveSpectrum.Bands;

    public VisualState State
    {
        get
        {
            if (!_heard)
            {
                return VisualState.Offline;
            }

            var age = Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastFrameTicks));
            if (!_lastRunning)
            {
                return VisualState.Paused; // a paused source sends one silent frame and then nothing
            }

            return age < Fresh ? VisualState.Live : VisualState.Offline;
        }
    }

    public bool TakeBands(float[] bands)
    {
        if (State != VisualState.Live || bands.Length < Bands)
        {
            return false;
        }

        lock (_gate)
        {
            Array.Copy(_pending, bands, Bands);
            Array.Clear(_pending);
        }

        return true;
    }

    public bool TakeChroma(float[] chroma)
    {
        if (State != VisualState.Live || chroma.Length < Chromagram.Notes)
        {
            return false;
        }

        lock (_gate)
        {
            Array.Copy(_pendingChroma, chroma, Chromagram.Notes);
            Array.Clear(_pendingChroma);
        }

        return true;
    }

    public float TakeBeat()
    {
        if (State != VisualState.Live)
        {
            return 0;
        }

        lock (_gate)
        {
            var beat = _pendingBeat;
            _pendingBeat = 0;
            return beat;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _run.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await SessionAsync(stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                // Unreachable name, network down, socket closed under us, or a bug: say so, wait and subscribe again.
                _log?.Invoke($"Visualizer: {e.GetType().Name}: {e.Message}");
            }

            _heard = false;
            _log?.Invoke("Visualizer: no data from the player, trying again in 3 s");
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task SessionAsync(CancellationToken stop)
    {
        var addresses = await Dns.GetHostAddressesAsync(_host, stop).ConfigureAwait(false);
        var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? throw new SocketException((int)SocketError.HostNotFound);
        var remote = new IPEndPoint(address, _port);
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        udp.Client.ReceiveBufferSize = 1 << 20;

        // The source only streams to clients it heard from in the last 2 s.
        var request = VismuxProtocol.BuildRequest();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var keepAlive = Task.Run(async () =>
        {
            try
            {
                while (!linked.IsCancellationRequested)
                {
                    udp.Client.SendTo(request, remote);
                    await Task.Delay(1000, linked.Token).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
            {
            }
        }, linked.Token);

        var ring = new SampleRing();
        var spectrum = new LiveSpectrum();
        var levels = new float[LiveSpectrum.Bands];
        var chromagram = new Chromagram();
        var chroma = new float[Chromagram.Notes];
        var onsets = new OnsetDetector();
        var clock = Stopwatch.StartNew();
        var lastAnalysis = TimeSpan.FromSeconds(-1); // not MinValue: the subtraction below would overflow
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var silence = CancellationTokenSource.CreateLinkedTokenSource(stop);
                silence.CancelAfter(TimeSpan.FromSeconds(10));
                UdpReceiveResult result;
                try
                {
                    result = await udp.ReceiveAsync(silence.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!stop.IsCancellationRequested)
                {
                    return; // ten seconds of nothing: resolve the name again and resubscribe
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset)
                {
                    continue; // ICMP "port unreachable" from an earlier send: vismux is not there (yet)
                }

                if (!VismuxProtocol.TryParseData(result.Buffer, out var frame) || frame is null)
                {
                    continue;
                }

                if (!_heard)
                {
                    _log?.Invoke($"Visualizer: receiving, {frame.Rate} Hz, {(frame.Running ? "playing" : "paused")}");
                }

                ring.Apply(frame);
                Interlocked.Exchange(ref _lastFrameTicks, Stopwatch.GetTimestamp());
                _lastRunning = frame.Running;
                _heard = true;

                // Every datagram, not only 30 times a second: a hit is over in a few milliseconds.
                var hit = onsets.Advance(ring, frame.BufIndex, frame.BufSize, frame.Rate);
                if (hit > 0 && frame.Running)
                {
                    lock (_gate)
                    {
                        _pendingBeat = Math.Max(_pendingBeat, hit);
                    }
                }

                if (!frame.Running || clock.Elapsed - lastAnalysis < TimeSpan.FromSeconds(1.0 / FramesPerSecond))
                {
                    continue;
                }

                lastAnalysis = clock.Elapsed;
                var banded = spectrum.Analyze(ring, frame.BufIndex, frame.Rate, levels);
                var pitched = chromagram.Analyze(ring, frame.BufIndex, frame.Rate, chroma);
                lock (_gate)
                {
                    for (var b = 0; b < LiveSpectrum.Bands && banded; b++)
                    {
                        _pending[b] = Math.Max(_pending[b], levels[b]);
                    }

                    for (var p = 0; p < Chromagram.Notes && pitched; p++)
                    {
                        _pendingChroma[p] = Math.Max(_pendingChroma[p], chroma[p]);
                    }
                }
            }
        }
        finally
        {
            linked.Cancel();
            await keepAlive.ConfigureAwait(false);
        }
    }
}
