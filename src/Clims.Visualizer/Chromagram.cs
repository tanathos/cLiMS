using Clims.Waveform;

namespace Clims.Visualizer;

/// <summary>
/// Turns the newest samples of the ring into the level of each note over five octaves, C3 (131 Hz) to B7 (3.9 kHz): a long
/// Hann-windowed FFT of the mono mix (a note a semitone apart from the next is only a few Hz apart at the bottom), its spectral
/// peaks placed on the nearest note, the louder ones counting more. A view with less room folds the octaves into the 12 pitch
/// classes (the chromagram proper).
/// </summary>
public sealed class Chromagram
{
    /// <summary>Notes in the result: five octaves.</summary>
    public const int Notes = 60;

    /// <summary>The MIDI number of the first note, C3 (middle C is 60).</summary>
    public const int FirstMidi = 48;

    private const int Size = 8192;
    private const double MaxOff = 0.35; // a peak further than this (in semitones) from a note is no note
    private const float QuietDb = -70f; // below this nothing is shown
    private const float LoudDb = -45f; // from this the chromagram is at full strength

    private readonly Fft _fft = new(Size);
    private readonly float[] _window = new float[Size];
    private readonly float[] _re = new float[Size];
    private readonly float[] _im = new float[Size];
    private readonly float[] _mag = new float[Size / 2];
    private readonly short[] _samples;

    /// <param name="channels">Interleaved channels in the ring (Squeezelite writes stereo).</param>
    public Chromagram(int channels = 2)
    {
        Channels = channels;
        _samples = new short[Size * channels];
        for (var i = 0; i < Size; i++)
        {
            _window[i] = 0.5f * (1 - MathF.Cos(2 * MathF.PI * i / (Size - 1)));
        }
    }

    public int Channels { get; }

    /// <summary>The frequency of a MIDI note.</summary>
    public static double Hz(int midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);

    /// <summary>Analyses the newest samples before <paramref name="bufIndex"/>; false while the ring does not hold enough yet.</summary>
    /// <param name="notes">Receives <see cref="Notes"/> values from 0 to 1, from <see cref="FirstMidi"/> up.</param>
    public bool Analyze(SampleRing ring, uint bufIndex, uint rate, float[] notes)
    {
        if (rate < 8000 || notes.Length < Notes)
        {
            return false;
        }

        var end = bufIndex - bufIndex % (uint)Channels;
        if (!ring.CopyLatest(end, _samples))
        {
            return false;
        }

        for (var i = 0; i < Size; i++)
        {
            var sum = 0;
            for (var c = 0; c < Channels; c++)
            {
                sum += _samples[i * Channels + c];
            }

            _re[i] = sum / (float)Channels / 32768f * _window[i];
            _im[i] = 0;
        }

        _fft.Transform(_re, _im);
        for (var k = 0; k < Size / 2; k++)
        {
            _mag[k] = MathF.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);
        }

        var low = Hz(FirstMidi - 1);
        var high = Math.Min(Hz(FirstMidi + Notes), rate / 2.0 * 0.9);
        var from = Math.Max(2, (int)Math.Floor(low * Size / rate));
        var to = Math.Min(Size / 2 - 2, (int)Math.Ceiling(high * Size / rate));
        Array.Clear(notes, 0, Notes);
        var loudest = 0f;
        for (var k = from; k <= to; k++)
        {
            var b = _mag[k];
            var a = _mag[k - 1];
            var c = _mag[k + 1];
            if (b <= a || b < c)
            {
                continue; // not the top of a peak
            }

            loudest = Math.Max(loudest, b);
            // The top of the parabola through the three bins is where the frequency really is.
            var denominator = a - 2 * b + c;
            var shift = denominator == 0 ? 0 : 0.5 * (a - c) / denominator;
            var hz = (k + shift) * rate / Size;
            var midi = 69 + 12 * Math.Log2(hz / 440);
            var note = Math.Round(midi);
            var index = (int)note - FirstMidi;
            if (Math.Abs(midi - note) > MaxOff || index < 0 || index >= Notes)
            {
                continue;
            }

            notes[index] += b;
        }

        // Against the loudest peak even when that was no note: a tone between two notes must not blow up the noise next to it.
        var strongest = Math.Max(notes.Take(Notes).Max(), loudest);
        if (strongest <= 0)
        {
            return true;
        }

        // The loudest peak against a full-scale sine (which reads 1 after the 4 / Size): nothing is shown for near silence.
        var db = 20 * MathF.Log10(loudest * 4f / Size + 1e-9f);
        var gate = Math.Clamp((db - QuietDb) / (LoudDb - QuietDb), 0f, 1f);
        for (var i = 0; i < Notes; i++)
        {
            var relative = notes[i] / strongest;
            notes[i] = relative * relative * gate; // squared: the notes that are really there stand out from the rest
        }

        return true;
    }
}
