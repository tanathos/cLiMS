using Clims.Waveform;

namespace Clims.Visualizer;

/// <summary>
/// Turns the newest samples of the ring into band levels: a Hann-windowed FFT of the mono mix, folded into log-spaced bands in dB
/// with the same slight tilt as the precomputed spectrum. A live feed has no "loudest moment of the track" to scale by, so the
/// top follows the recent peak and sinks slowly.
/// </summary>
public sealed class LiveSpectrum
{
    public const int Bands = 32;

    private const int Size = 2048;
    // Narrower than the 55 dB of the precomputed spectrum: dense music has all its bands within a few dB of each other, and with
    // 55 dB they all sat near the top and the bars hardly moved.
    private const float RangeDb = 40f;
    private const float FloorTopDb = -20f; // never scale the top lower than this, or silence would fill the bars
    private const float SinkDbPerFrame = 0.1f; // 3 dB a second at 30 analyses a second
    private const double LowHz = 40;
    private const double HighHz = 16000;

    private readonly Fft _fft = new(Size);
    private readonly float[] _window = new float[Size];
    private readonly float[] _re = new float[Size];
    private readonly float[] _im = new float[Size];
    private readonly float[] _db = new float[Bands];
    private readonly short[] _samples;
    private (int From, int To)[] _bins = [];
    private float[] _tilt = [];
    private uint _binsRate;
    private float _top = FloorTopDb;

    /// <param name="channels">Interleaved channels in the ring (Squeezelite writes stereo).</param>
    public LiveSpectrum(int channels = 2)
    {
        Channels = channels;
        _samples = new short[Size * channels];
        for (var i = 0; i < Size; i++)
        {
            _window[i] = 0.5f * (1 - MathF.Cos(2 * MathF.PI * i / (Size - 1)));
        }
    }

    public int Channels { get; }

    /// <summary>Analyses the newest samples before <paramref name="bufIndex"/>; false while the ring does not hold enough yet.</summary>
    /// <param name="levels">Receives <see cref="Bands"/> values from 0 to 1.</param>
    public bool Analyze(SampleRing ring, uint bufIndex, uint rate, float[] levels)
    {
        if (rate < 8000 || levels.Length < Bands)
        {
            return false;
        }

        // Start on a whole frame (a left sample), whatever the index the source stopped at.
        var end = bufIndex - bufIndex % (uint)Channels;
        if (!ring.CopyLatest(end, _samples))
        {
            return false;
        }

        EnsureBins(rate);
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
        var scale = 4f / Size; // a full-scale sine reads about 0 dB
        var loudest = float.NegativeInfinity;
        for (var b = 0; b < Bands; b++)
        {
            var (from, to) = _bins[b];
            var power = 0f;
            for (var k = from; k < to; k++)
            {
                power = Math.Max(power, _re[k] * _re[k] + _im[k] * _im[k]);
            }

            _db[b] = 10 * MathF.Log10(power * scale * scale + 1e-12f) + _tilt[b];
            loudest = Math.Max(loudest, _db[b]);
        }

        _top = Math.Max(Math.Max(_top - SinkDbPerFrame, loudest), FloorTopDb);
        var floor = _top - RangeDb;
        for (var b = 0; b < Bands; b++)
        {
            levels[b] = Math.Clamp((_db[b] - floor) / RangeDb, 0f, 1f);
        }

        return true;
    }

    private void EnsureBins(uint rate)
    {
        if (rate == _binsRate)
        {
            return;
        }

        _binsRate = rate;
        _bins = new (int, int)[Bands];
        _tilt = new float[Bands];
        var high = Math.Min(HighHz, rate / 2.0 * 0.95);
        for (var b = 0; b < Bands; b++)
        {
            var lo = LowHz * Math.Pow(high / LowHz, b / (double)Bands);
            var hi = LowHz * Math.Pow(high / LowHz, (b + 1) / (double)Bands);
            var from = Math.Clamp((int)Math.Floor(lo * Size / rate), 1, Size / 2 - 1);
            var to = Math.Clamp(Math.Max((int)Math.Ceiling(hi * Size / rate), from + 1), from + 1, Size / 2);
            _bins[b] = (from, to);
            _tilt[b] = (float)(3 * Math.Log2(Math.Sqrt(lo * hi) / 1000)); // +3 dB per octave above 1 kHz, minus below
        }
    }
}
