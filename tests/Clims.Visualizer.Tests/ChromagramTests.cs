namespace Clims.Visualizer.Tests;

public class ChromagramTests
{
    private const uint Rate = 44100;
    private const uint RingSize = 16384;

    /// <summary>Index of a note in the result: its MIDI number minus the first note, C3.</summary>
    private static int Index(int midi) => midi - Chromagram.FirstMidi;

    private static readonly int C4 = Index(60), E4 = Index(64), G4 = Index(67);

    /// <summary>A stereo ring (interleaved) holding the sum of sines, each of its own amplitude, in both channels.</summary>
    private static SampleRing Ring(params (double Hz, double Amplitude)[] tones)
    {
        var samples = new short[RingSize];
        for (var i = 0; i < RingSize / 2; i++)
        {
            var v = 0.0;
            foreach (var (hz, amplitude) in tones)
            {
                v += Math.Sin(2 * Math.PI * hz * i / Rate) * amplitude;
            }

            samples[i * 2] = samples[i * 2 + 1] = (short)(v * short.MaxValue);
        }

        var ring = new SampleRing();
        ring.Apply(new VismuxFrame(1, RingSize, 0, Rate, true, samples));
        return ring;
    }

    private static float[] Analyze(SampleRing ring)
    {
        var chroma = new float[Chromagram.Notes];
        Assert.True(new Chromagram().Analyze(ring, 0, Rate, chroma));
        return chroma;
    }

    [Theory]
    [InlineData(130.81, 48)] // C3, the lowest note
    [InlineData(220.0, 57)] // A3
    [InlineData(440.0, 69)] // A4: the octaves are notes of their own
    [InlineData(880.0, 81)] // A5
    [InlineData(1760.0, 93)] // A6
    [InlineData(3520.0, 105)] // A7, near the top
    [InlineData(261.63, 60)] // middle C
    [InlineData(392.0, 67)] // G4
    public void A_single_note_lights_its_own_bar(double hz, int midi)
    {
        var expected = Index(midi);
        var chroma = Analyze(Ring((hz, 0.5)));

        Assert.Equal(expected, Array.IndexOf(chroma, chroma.Max()));
        Assert.Equal(1f, chroma[expected], 2);
        Assert.All(chroma.Where((_, i) => i != expected), v => Assert.True(v < 0.2f, $"{v}"));
    }

    [Fact]
    public void A_c_major_chord_lights_c_e_and_g_of_its_octave()
    {
        var chroma = Analyze(Ring((261.63, 0.25), (329.63, 0.25), (392.0, 0.25)));

        var top = chroma.Select((v, i) => (v, i)).OrderByDescending(t => t.v).Take(3).Select(t => t.i).OrderBy(i => i).ToArray();

        Assert.Equal([C4, E4, G4], top);
        Assert.All(new[] { C4, E4, G4 }, n => Assert.True(chroma[n] > 0.6f, $"{n}: {chroma[n]}"));
    }

    [Fact]
    public void A_tone_between_two_notes_is_no_note()
    {
        // Halfway between A4 (440) and A#4 (466.16).
        var chroma = Analyze(Ring((453.0, 0.5)));

        Assert.All(chroma, v => Assert.True(v < 0.001f, $"{v}"));
    }

    [Fact]
    public void Near_silence_shows_nothing()
    {
        var quiet = Analyze(Ring((440, 0.0001)));
        var none = Analyze(Ring());

        Assert.All(quiet, v => Assert.True(v < 0.05f));
        Assert.All(none, v => Assert.Equal(0f, v));
    }
}
