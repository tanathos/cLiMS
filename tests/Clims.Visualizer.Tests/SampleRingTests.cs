namespace Clims.Visualizer.Tests;

public class SampleRingTests
{
    private static VismuxFrame Frame(uint size, uint index, params short[] samples) => new(1, size, index, 44100, true, samples);

    [Fact]
    public void A_full_frame_fills_the_ring()
    {
        var ring = new SampleRing();
        ring.Apply(Frame(4, 0, 1, 2, 3, 4));

        var latest = new short[4];
        Assert.True(ring.CopyLatest(0, latest)); // index 0 = just wrapped: the whole ring in order
        Assert.Equal(new short[] { 1, 2, 3, 4 }, latest);
    }

    [Fact]
    public void A_delta_ends_at_its_index()
    {
        var ring = new SampleRing();
        ring.Apply(Frame(8, 0, 1, 2, 3, 4, 5, 6, 7, 8));
        ring.Apply(Frame(8, 5, 90, 91)); // overwrites positions 3 and 4

        var latest = new short[8];
        Assert.True(ring.CopyLatest(5, latest));
        Assert.Equal(new short[] { 6, 7, 8, 1, 2, 3, 90, 91 }, latest);
    }

    [Fact]
    public void A_delta_wraps_around_the_end_of_the_ring()
    {
        var ring = new SampleRing();
        ring.Apply(Frame(8, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        ring.Apply(Frame(8, 1, 10, 11, 12)); // positions 6, 7 and 0

        var latest = new short[3];
        Assert.True(ring.CopyLatest(1, latest));
        Assert.Equal(new short[] { 10, 11, 12 }, latest);
    }

    [Fact]
    public void Peak_looks_at_the_newest_samples_only()
    {
        var ring = new SampleRing();
        ring.Apply(Frame(8, 0, 30000, 0, 0, 0, 0, 0, -500, 400));

        Assert.Equal(500, ring.Peak(0, 2));
        Assert.Equal(30000, ring.Peak(0, 8));
    }

    [Fact]
    public void An_empty_ring_gives_nothing()
    {
        var ring = new SampleRing();

        Assert.False(ring.CopyLatest(0, new short[4]));
        Assert.Equal(0, ring.Peak(0, 4));
    }
}
