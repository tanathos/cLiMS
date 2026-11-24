namespace Clims.Visualizer.Tests;

public class LiveSpectrumTests
{
    private const uint Rate = 44100;
    private const uint RingSize = 16384;

    /// <summary>A stereo ring (interleaved) holding the same sine in both channels.</summary>
    private static SampleRing SineRing(double hz, double amplitude)
    {
        var samples = new short[RingSize];
        for (var i = 0; i < RingSize / 2; i++)
        {
            var v = (short)(Math.Sin(2 * Math.PI * hz * i / Rate) * amplitude * short.MaxValue);
            samples[i * 2] = v;
            samples[i * 2 + 1] = v;
        }

        var ring = new SampleRing();
        ring.Apply(new VismuxFrame(1, RingSize, 0, Rate, true, samples));
        return ring;
    }

    [Fact]
    public void A_sine_lights_the_band_that_holds_its_frequency()
    {
        var ring = SineRing(1000, 0.8);
        var levels = new float[LiveSpectrum.Bands];

        Assert.True(new LiveSpectrum().Analyze(ring, 0, Rate, levels));

        var loudest = Array.IndexOf(levels, levels.Max());
        // Bands are log-spaced from 40 Hz to 16 kHz: 1 kHz sits at about 0.6 of the way.
        Assert.InRange(loudest, 17, 22);
        Assert.Equal(1f, levels[loudest], 2);
        Assert.True(levels[0] < 0.3f);
        Assert.True(levels[LiveSpectrum.Bands - 1] < 0.3f);
    }

    [Fact]
    public void Silence_stays_at_the_bottom()
    {
        var ring = new SampleRing();
        ring.Apply(new VismuxFrame(1, RingSize, 0, Rate, true, new short[RingSize]));
        var levels = new float[LiveSpectrum.Bands];

        Assert.True(new LiveSpectrum().Analyze(ring, 0, Rate, levels));
        Assert.All(levels, l => Assert.Equal(0f, l));
    }

    [Fact]
    public void Nothing_is_analysed_before_the_ring_has_data_or_the_rate_is_known()
    {
        var levels = new float[LiveSpectrum.Bands];

        Assert.False(new LiveSpectrum().Analyze(new SampleRing(), 0, Rate, levels));
        Assert.False(new LiveSpectrum().Analyze(SineRing(1000, 0.5), 0, 0, levels));
    }
}
