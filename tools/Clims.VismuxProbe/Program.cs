using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Clims.Visualizer;

if (args.Length < 1 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: Clims.VismuxProbe <player-ip-or-host> [port=23483] [seconds=10]");
    return 2;
}

var port = args.Length > 1 ? int.Parse(args[1]) : VismuxProtocol.DefaultPort;
var seconds = args.Length > 2 ? int.Parse(args[2]) : 10;
var address = (await Dns.GetHostAddressesAsync(args[0])).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
if (address is null)
{
    Console.WriteLine($"No IPv4 address for {args[0]}.");
    return 2;
}

var remote = new IPEndPoint(address, port);
using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
udp.Client.ReceiveBufferSize = 1 << 20;
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var request = VismuxProtocol.BuildRequest();
long received = 0;

// The source only streams to clients it heard from in the last 2 s, so keep asking once a second.
var keepAlive = Task.Run(async () =>
{
    var warned = false;
    while (!cts.IsCancellationRequested)
    {
        udp.Client.SendTo(request, remote);
        try { await Task.Delay(1000, cts.Token); } catch (OperationCanceledException) { return; }
        if (!warned && Interlocked.Read(ref received) == 0 && !cts.IsCancellationRequested)
        {
            Console.WriteLine("  ...no reply yet. Is vismux running on the player, and is UDP 23483 reachable?");
            warned = true;
        }
    }
});

Console.WriteLine($"Subscribing to {remote} for {seconds} s (Ctrl+C to stop). Play something on the player.");

var ring = new SampleRing();
var clock = Stopwatch.StartNew();
var lastPrint = TimeSpan.Zero;
uint? lastSequence = null;
long datagrams = 0, full = 0, delta = 0, missing = 0, runningFrames = 0, bytes = 0, audibleFrames = 0;
var peakSinceLastPrint = 0;
uint rate = 0;

while (!cts.IsCancellationRequested)
{
    UdpReceiveResult result;
    try { result = await udp.ReceiveAsync(cts.Token); }
    catch (OperationCanceledException) { break; }
    catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset) { continue; } // ICMP "port unreachable" from an earlier send

    Interlocked.Increment(ref received);
    var datagram = result.Buffer;
    if (VismuxProtocol.TryParseSubAck(datagram) is { } mac)
    {
        Console.WriteLine($"  subscribed, source MAC {mac}");
        continue;
    }
    if (!VismuxProtocol.TryParseData(datagram, out var frame) || frame is null)
        continue;

    datagrams++;
    bytes += datagram.Length;
    if (frame.IsFull) full++; else delta++;
    if (lastSequence is { } last && frame.Sequence > last + 1)
        missing += frame.Sequence - last - 1;
    lastSequence = frame.Sequence;
    rate = frame.Rate;

    ring.Apply(frame);
    var peak = ring.Peak(frame.BufIndex, 1024);
    peakSinceLastPrint = Math.Max(peakSinceLastPrint, peak);
    if (frame.Running)
    {
        runningFrames++;
        if (peak > 0) audibleFrames++;
    }

    if (clock.Elapsed - lastPrint >= TimeSpan.FromMilliseconds(500))
    {
        lastPrint = clock.Elapsed;
        var db = peakSinceLastPrint == 0 ? -90.0 : 20 * Math.Log10(peakSinceLastPrint / 32768.0);
        var bar = new string('#', (int)Math.Clamp((db + 60) / 60 * 40, 0, 40));
        Console.WriteLine($"  seq {frame.Sequence,7} {(frame.IsFull ? "full " : "delta")} {frame.Samples.Length,5} smp  {frame.Rate} Hz  {(frame.Running ? "running" : "paused ")}  {db,6:F1} dBFS |{bar,-40}|");
        peakSinceLastPrint = 0;
    }
}

cts.Cancel();
await keepAlive;

var elapsed = Math.Max(clock.Elapsed.TotalSeconds, 0.001);
Console.WriteLine();
Console.WriteLine($"Data frames: {datagrams} ({datagrams / elapsed:F0}/s, {bytes / 1024.0 / elapsed:F0} KiB/s), full {full}, delta {delta}, sequence gaps {missing}, rate {rate} Hz");
Console.WriteLine($"While running: {runningFrames} frames, {audibleFrames} with non-zero samples.");

if (datagrams == 0)
{
    Console.WriteLine("RESULT: nothing received. Check vismux on the player (ps | grep vismux), the -v option and UDP 23483 in any firewall.");
    return 3;
}
if (audibleFrames == 0)
{
    Console.WriteLine("RESULT: the stream works but carried no sound. Was a track playing (not paused) during the test?");
    return 1;
}
Console.WriteLine("RESULT: OK, live samples are arriving.");
return 0;
