namespace Clims.Visualizer.Tests;

public class OnsetDetectorTests
{
    private const uint Rate = 44100;
    private const uint RingSize = 16384;
    private const int Chunk = 882; // the 20 ms of one vismux delta

    /// <summary>Feeds a mono signal to a ring as the player would, and returns the time in seconds of each hit found.</summary>
    private static List<double> Hits(double seconds, Func<double, Random, double> signal)
    {
        var random = new Random(1);
        var ring = new SampleRing();
        var detector = new OnsetDetector();
        ring.Apply(new VismuxFrame(0, RingSize, 0, Rate, true, new short[RingSize]));
        var hits = new List<double>();
        uint index = 0;
        for (var done = 0; done < seconds * Rate; done += Chunk)
        {
            var samples = new short[Chunk * 2];
            for (var i = 0; i < Chunk; i++)
            {
                var v = Math.Clamp(signal((done + i) / (double)Rate, random), -1, 1);
                samples[i * 2] = samples[i * 2 + 1] = (short)(v * short.MaxValue);
            }

            index = (index + (uint)samples.Length) % RingSize;
            ring.Apply(new VismuxFrame(1, RingSize, index, Rate, true, samples));
            if (detector.Advance(ring, index, RingSize, Rate) > 0)
            {
                hits.Add(done / (double)Rate);
            }
        }

        return hits;
    }

    private static double Noise(Random random) => random.NextDouble() * 2 - 1;

    /// <summary>A loud low chord and a steady hiss: dense music without drums.</summary>
    private static double Wall(double t, Random random) =>
        0.2 * Math.Sin(2 * Math.PI * 82 * t) + 0.2 * Math.Sin(2 * Math.PI * 330 * t) + 0.1 * Math.Sin(2 * Math.PI * 2500 * t) + 0.05 * Noise(random);

    [Fact]
    public void Snare_like_bursts_over_a_dense_background_are_each_found_once()
    {
        // A short burst of noise every 0.4 s, from 1 s on.
        var hits = Hits(5, (t, r) => Wall(t, r) + (t >= 1 && (t - 1) % 0.4 < 0.05 ? 0.5 * Noise(r) * Math.Exp(-((t - 1) % 0.4) * 60) : 0))
            .Where(h => h > 0.5).ToList(); // the start of the sound itself is a rise

        Assert.Equal(10, hits.Count);
        Assert.All(hits, h => Assert.InRange((h - 1) % 0.4, 0, 0.09)); // within a tenth of a second of its burst
    }

    [Fact]
    public void A_dense_background_without_drums_does_not_flash()
    {
        var hits = Hits(5, Wall);

        Assert.DoesNotContain(hits, h => h > 1); // the start of the sound itself is a rise
    }

    [Fact]
    public void Low_thumps_are_not_taken_for_the_snare()
    {
        var hits = Hits(5, (t, r) => 0.05 * Noise(r) + 0.6 * Math.Sin(2 * Math.PI * 60 * t) * Math.Exp(-(t % 0.5) * 20));

        Assert.DoesNotContain(hits, h => h > 1);
    }

    [Fact]
    public void Silence_has_no_hits() => Assert.Empty(Hits(2, (_, _) => 0));
}
