namespace Clims.Waveform;

/// <summary>
/// Turns a stream of mono samples into a <see cref="SpectrumProfile"/>: a Hann-windowed FFT every 1/<see cref="FramesPerSecond"/>
/// second, folded into log-spaced bands, in dB with a slight tilt (music has far more energy in the bass than the treble), and
/// scaled at the end so the loudest moment of the track reaches the top.
/// </summary>
internal sealed class SpectrumBuilder
{
    public const int FramesPerSecond = 20;
    public const int Bands = 32;

    private const float RangeDb = 55f;
    private const double LowHz = 40;
    private const double HighHz = 16000;

    private readonly int _hop;
    private readonly Fft _fft;
    private readonly float[] _window;
    private readonly float[] _buffer;
    private readonly float[] _re;
    private readonly float[] _im;
    private readonly (int From, int To)[] _bins;
    private readonly float[] _tilt;
    private readonly List<float[]> _frames = new();
    private int _filled;
    private int _skip;

    public SpectrumBuilder(int sampleRate)
    {
        var size = sampleRate <= 50000 ? 2048 : 4096;
        _fft = new Fft(size);
        _hop = Math.Max(1, sampleRate / FramesPerSecond);
        _window = new float[size];
        for (var i = 0; i < size; i++)
        {
            _window[i] = 0.5f * (1 - MathF.Cos(2 * MathF.PI * i / (size - 1)));
        }

        _buffer = new float[size];
        _filled = size / 2; // the first frame is centred on second 0: half of its window lies before the start (silence)
        _re = new float[size];
        _im = new float[size];

        var high = Math.Min(HighHz, sampleRate / 2.0 * 0.95);
        _bins = new (int, int)[Bands];
        _tilt = new float[Bands];
        for (var b = 0; b < Bands; b++)
        {
            var lo = LowHz * Math.Pow(high / LowHz, b / (double)Bands);
            var hi = LowHz * Math.Pow(high / LowHz, (b + 1) / (double)Bands);
            var from = (int)Math.Floor(lo * size / sampleRate);
            var to = (int)Math.Ceiling(hi * size / sampleRate);
            from = Math.Clamp(from, 1, size / 2 - 1);
            to = Math.Clamp(Math.Max(to, from + 1), from + 1, size / 2);
            _bins[b] = (from, to);
            _tilt[b] = (float)(3 * Math.Log2(Math.Sqrt(lo * hi) / 1000)); // +3 dB per octave above 1 kHz, minus below
        }
    }

    public void Push(float sample)
    {
        if (_skip > 0)
        {
            _skip--;
            return;
        }

        _buffer[_filled++] = sample;
        if (_filled < _buffer.Length)
        {
            return;
        }

        Compute();
        if (_hop >= _buffer.Length)
        {
            _skip = _hop - _buffer.Length;
            _filled = 0;
        }
        else
        {
            Array.Copy(_buffer, _hop, _buffer, 0, _buffer.Length - _hop);
            _filled = _buffer.Length - _hop;
        }
    }

    private void Compute()
    {
        for (var i = 0; i < _buffer.Length; i++)
        {
            _re[i] = _buffer[i] * _window[i];
            _im[i] = 0;
        }

        _fft.Transform(_re, _im);
        var frame = new float[Bands];
        var scale = 4f / _buffer.Length; // a full-scale sine reads about 0 dB
        for (var b = 0; b < Bands; b++)
        {
            var (from, to) = _bins[b];
            var power = 0f;
            for (var k = from; k < to; k++)
            {
                power = Math.Max(power, _re[k] * _re[k] + _im[k] * _im[k]);
            }

            frame[b] = 10 * MathF.Log10(power * scale * scale + 1e-12f) + _tilt[b];
        }

        _frames.Add(frame);
    }

    public SpectrumProfile Finish()
    {
        var loudest = float.NegativeInfinity;
        foreach (var f in _frames)
        {
            foreach (var v in f)
            {
                loudest = Math.Max(loudest, v);
            }
        }

        var levels = new byte[_frames.Count * Bands];
        if (_frames.Count > 0 && loudest > -100)
        {
            var floor = loudest - RangeDb;
            for (var i = 0; i < _frames.Count; i++)
            {
                for (var b = 0; b < Bands; b++)
                {
                    var x = Math.Clamp((_frames[i][b] - floor) / RangeDb, 0f, 1f);
                    levels[i * Bands + b] = (byte)MathF.Round(x * 255);
                }
            }
        }

        return new SpectrumProfile(levels, Bands, FramesPerSecond);
    }
}
