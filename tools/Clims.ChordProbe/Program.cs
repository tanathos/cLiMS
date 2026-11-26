using System.Globalization;
using Clims.ChordProbe;

if (args.Length < 1 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: Clims.ChordProbe <audio-file> [--switch-cost 5] [--labels out.txt]");
    return 2;
}

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string? Option(string name) => Array.IndexOf(args, name) is var i && i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
var switchCost = float.Parse(Option("--switch-cost") ?? "5", CultureInfo.InvariantCulture);

var (samples, rate) = AudioDecoder.Decode(args[0]);
var (frames, hop, tuning, agreement) =ChromaExtractor.Extract(samples, rate);
var chords = Chord.Vocabulary();
var path = ChordDecoder.Decode(frames, chords, switchCost, causal: false);
var live = ChordDecoder.Decode(frames, chords, switchCost, causal: true);

var offset = hop * 1.5; // a frame is four hops long and is named after its middle hop
var segments = new List<ChordSegment>();
for (var t = 0; t < path.Length; t++)
{
    var name = chords[path[t]].Name;
    if (segments.Count > 0 && segments[^1].Name == name)
    {
        segments[^1] = segments[^1] with { End = (t + 1) * hop + offset };
    }
    else
    {
        segments.Add(new ChordSegment(t * hop + offset, (t + 1) * hop + offset, name));
    }
}

static string Time(double s) => $"{(int)s / 60}:{(int)s % 60:00}";

var duration = samples.Length / (double)rate;
var same = path.Zip(live).Count(p => p.First == p.Second) / (double)path.Length;
var shares = segments.GroupBy(s => s.Name)
    .Select(g => (g.Key, Share: g.Sum(s => s.End - s.Start) / duration))
    .OrderByDescending(g => g.Share)
    .Take(8);

Console.WriteLine($"Duration {Time(duration)}, {rate} Hz, tuning {tuning:+0;-0;0} cents (peaks agree {agreement:F2} of 1)");
Console.WriteLine($"{segments.Count} chord segments, average {duration / segments.Count:F1} s; causal (live-like) decoding agrees on {same:P0} of the time");
Console.WriteLine("Most time: " + string.Join(", ", shares.Select(g => $"{g.Key} {g.Share:P0}")));
Console.WriteLine();
foreach (var segment in segments)
{
    Console.WriteLine($"{Time(segment.Start),6}  {segment.End - segment.Start,5:F1}s  {segment.Name}");
}

if (Option("--labels") is { } labels)
{
    // Audacity label track: File > Import > Labels.
    File.WriteAllLines(labels, segments.Select(s => $"{s.Start:F3}\t{s.End:F3}\t{s.Name}"));
}

return 0;
