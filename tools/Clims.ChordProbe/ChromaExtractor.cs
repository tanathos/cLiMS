using Clims.Waveform;

namespace Clims.ChordProbe;

/// <summary>
/// Samples to chroma: long FFTs, a median over time of every bin (drum hits are short, notes are not), the spectral peaks, one
/// tuning offset for the whole track, and the peaks folded into pitch classes.
/// </summary>
internal static class ChromaExtractor
{
    private const int Median = 5; // slices, about half a second

    /// <returns>The frames, the time between them, the tuning offset and how much the peaks agree on it (0 to 1).</returns>
    public static (List<ChromaFrame> Frames, double HopSeconds, double TuningCents, double TuningAgreement) Extract(float[] samples, int rate)
    {
        var size = rate > 60000 ? 32768 : 16384; // about 0.37 s: 2.7 Hz a bin
        var hop = size / 4;
        var fft = new Fft(size);
        var window = new float[size];
        for (var i = 0; i < size; i++)
        {
            window[i] = 0.5f * (1 - MathF.Cos(2 * MathF.PI * i / (size - 1)));
        }

        var bins = Math.Min(size / 2, 2200 * size / rate); // up to about C7
        var magnitudes = new List<float[]>();
        var levels = new List<float>();
        var re = new float[size];
        var im = new float[size];
        for (var start = 0; start + size <= samples.Length; start += hop)
        {
            var power = 0.0;
            for (var i = 0; i < size; i++)
            {
                var v = samples[start + i];
                power += v * v;
                re[i] = v * window[i];
                im[i] = 0;
            }

            fft.Transform(re, im);
            var magnitude = new float[bins];
            for (var k = 0; k < bins; k++)
            {
                magnitude[k] = MathF.Sqrt(re[k] * re[k] + im[k] * im[k]) * 4f / size;
            }

            magnitudes.Add(magnitude);
            levels.Add((float)(10 * Math.Log10(power / size + 1e-12)));
        }

        var peaks = new List<List<SpectralPeak>>();
        var column = new float[Median];
        var steady = new float[bins];
        for (var t = 0; t < magnitudes.Count; t++)
        {
            for (var k = 0; k < bins; k++)
            {
                for (var d = 0; d < Median; d++)
                {
                    column[d] = magnitudes[Math.Clamp(t + d - Median / 2, 0, magnitudes.Count - 1)][k];
                }

                Array.Sort(column);
                steady[k] = column[Median / 2];
            }

            peaks.Add(Peaks(steady, rate, size));
        }

        // Circular mean of how far the peaks are from the nearest note: -50 and +50 cents are the same place.
        double x = 0, y = 0, total = 0;
        foreach (var peak in peaks.SelectMany(p => p))
        {
            total += peak.Magnitude;
            var angle = 2 * Math.PI * (peak.Midi - Math.Round(peak.Midi));
            x += Math.Cos(angle) * peak.Magnitude;
            y += Math.Sin(angle) * peak.Magnitude;
        }

        var tuning = Math.Atan2(y, x) / (2 * Math.PI); // in semitones
        var frames = peaks.Select((p, t) => Fold(p, tuning, levels[t])).ToList();
        return (frames, hop / (double)rate, tuning * 100, total > 0 ? Math.Sqrt(x * x + y * y) / total : 0);
    }

    private static List<SpectralPeak> Peaks(float[] magnitude, int rate, int size)
    {
        var sums = new double[magnitude.Length + 1];
        for (var k = 0; k < magnitude.Length; k++)
        {
            sums[k + 1] = sums[k] + magnitude[k];
        }

        var peaks = new List<SpectralPeak>();
        for (var k = Math.Max(2, 40 * size / rate); k < magnitude.Length - 1; k++)
        {
            var (a, b, c) = (magnitude[k - 1], magnitude[k], magnitude[k + 1]);
            if (b <= a || b < c || b < 3e-4f)
            {
                continue;
            }

            // Only what stands out from its surroundings: distortion and cymbals fill the rest.
            var from = Math.Max(0, k - 40);
            var to = Math.Min(magnitude.Length, k + 41);
            if (b < 2 * (sums[to] - sums[from]) / (to - from))
            {
                continue;
            }

            var denominator = a - 2 * b + c;
            var shift = denominator == 0 ? 0 : 0.5 * (a - c) / denominator;
            var hz = (k + shift) * rate / size;
            peaks.Add(new SpectralPeak(69 + 12 * Math.Log2(hz / 440), b));
        }

        return peaks;
    }

    private static ChromaFrame Fold(List<SpectralPeak> peaks, double tuning, float level)
    {
        var treble = new float[12];
        var bass = new float[12];
        foreach (var peak in peaks)
        {
            var midi = peak.Midi - tuning;
            var note = (int)Math.Round(midi);
            var fit = Math.Cos(Math.PI * (midi - note)); // 1 on the note, 0 halfway to the next
            var weight = (float)(fit * fit * Math.Sqrt(peak.Magnitude)); // square root: loud notes must not drown the others
            if (note is >= 48 and < 84)
            {
                treble[note % 12] += weight;
            }

            if (note is >= 28 and < 52)
            {
                bass[note % 12] += weight;
            }
        }

        var length = MathF.Sqrt(treble.Sum(v => v * v));
        var strongest = bass.Max();
        for (var i = 0; i < 12; i++)
        {
            treble[i] = length > 0 ? treble[i] / length : 0;
            bass[i] = strongest > 0 ? bass[i] / strongest : 0;
        }

        return new ChromaFrame(treble, bass, level);
    }
}
