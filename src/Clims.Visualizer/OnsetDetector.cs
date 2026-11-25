using Clims.Waveform;

namespace Clims.Visualizer;

/// <summary>
/// Finds the drum hits, the snare above all, in the samples of what is being played. Every 12 ms it measures by how many dB the
/// bands from 1.5 to 11 kHz rose: the crack of a snare rises all of them at once, which bass, guitars and voices do not, however
/// dense the mix. A hit is a rise that tops its neighbours, stands well above the median of the last second and a half, and is not
/// small next to the recent hits. It works on the samples, not on the levels drawn: those are squeezed against the top in loud music.
/// </summary>
public sealed class OnsetDetector
{
    private const double LowHz = 1500;
    private const double HighHz = 11000;
    private const int Bands = 24;
    private const float RangeDb = 50f; // bands this far below the recent loudest are level with each other: their noise is no hit
    private const float QuietTopDb = -50f; // never scale lower than this, or the noise in silence would be music
    private const float SinkDbPerHop = 0.03f;
    private const float MarginDb = 1.8f; // a hit rises this much more than the median rise
    private const float OfPeak = 0.55f; // and this much of the recent largest rise
    private const float PeakSeconds = 3f; // how fast the memory of the largest rise fades
    private const float MedianSeconds = 1.5f;
    private const float GapSeconds = 0.2f;
    private const int Around = 3; // hops on each side that a hit must top, which makes it known 35 ms late

    private readonly int _channels;
    private readonly List<float> _rises = []; // the newest last; long enough for the median and the neighbours
    private Fft _fft = new(1024);
    private float[] _window = [];
    private float[] _re = [];
    private float[] _im = [];
    private short[] _audio = []; // the last window of samples, interleaved
    private readonly float[][] _db = [new float[Bands], new float[Bands], new float[Bands]]; // [0] is the newest
    private (int From, int To)[] _bins = [];
    private uint _rate;
    private int _hop; // samples (all channels) between two measures
    private uint _position;
    private int _filled;
    private int _hops;
    private int _sinceHit;
    private float _top = QuietTopDb;
    private float _peak;

    /// <param name="channels">Interleaved channels in the ring (Squeezelite writes stereo).</param>
    public OnsetDetector(int channels = 2) => _channels = channels;

    /// <summary>
    /// Looks at the samples that arrived since the last call, up to <paramref name="bufIndex"/>. Returns the strength of the
    /// strongest hit found, 0.6 to 1, or 0 when there is none.
    /// </summary>
    /// <param name="ring">The samples.</param>
    /// <param name="bufIndex">Where the source stopped writing.</param>
    /// <param name="bufSize">Size of the source ring in samples.</param>
    /// <param name="rate">Sample rate in Hz.</param>
    public float Advance(SampleRing ring, uint bufIndex, uint bufSize, uint rate)
    {
        if (rate < 8000 || bufSize == 0)
        {
            return 0;
        }

        var end = bufIndex - bufIndex % (uint)_channels;
        if (rate != _rate)
        {
            Start(rate);
            _position = end;
            return 0;
        }

        var arrived = (int)((end + bufSize - _position) % bufSize);
        var take = arrived - arrived % _hop;
        if (take == 0)
        {
            return 0;
        }

        _position = (uint)((_position + take) % bufSize);
        // The samples up to the new position, which may stop a little short of what has arrived.
        var chunk = new short[take];
        if (!ring.CopyLatest(_position, chunk))
        {
            return 0;
        }

        var strongest = 0f;
        for (var at = 0; at < take; at += _hop)
        {
            strongest = Math.Max(strongest, Add(chunk.AsSpan(at, _hop)));
        }

        return strongest;
    }

    private void Start(uint rate)
    {
        _rate = rate;
        var size = rate > 60000 ? 2048 : 1024; // about 23 ms either way
        _fft = new Fft(size);
        _window = new float[size];
        for (var i = 0; i < size; i++)
        {
            _window[i] = 0.5f * (1 - MathF.Cos(2 * MathF.PI * i / (size - 1)));
        }

        _re = new float[size];
        _im = new float[size];
        _audio = new short[size * _channels];
        _hop = size / 2 * _channels;
        _bins = new (int, int)[Bands];
        var high = Math.Min(HighHz, rate / 2.0 * 0.95);
        var low = Math.Min(LowHz, high / 2);
        for (var b = 0; b < Bands; b++)
        {
            var from = (int)Math.Round(low * Math.Pow(high / low, b / (double)Bands) * size / rate);
            var to = (int)Math.Round(low * Math.Pow(high / low, (b + 1) / (double)Bands) * size / rate);
            from = Math.Clamp(from, 1, size / 2 - 1);
            _bins[b] = (from, Math.Clamp(to, from + 1, size / 2));
        }

        _rises.Clear();
        _filled = 0;
        _hops = 0;
        _sinceHit = int.MaxValue / 2;
        _top = QuietTopDb;
        _peak = 0;
    }

    /// <summary>Takes in one hop of samples; returns the strength of the hit that it confirms, or 0.</summary>
    private float Add(ReadOnlySpan<short> hop)
    {
        Array.Copy(_audio, hop.Length, _audio, 0, _audio.Length - hop.Length);
        hop.CopyTo(_audio.AsSpan(_audio.Length - hop.Length));
        _filled = Math.Min(_audio.Length, _filled + hop.Length);
        if (_filled < _audio.Length)
        {
            return 0;
        }

        var rise = Rise();
        var perSecond = _rate / (float)(_hop / _channels);
        _rises.Add(rise);
        var history = (int)(MedianSeconds * perSecond);
        if (_rises.Count > history + Around + 1)
        {
            _rises.RemoveAt(0);
        }

        _peak *= MathF.Exp(-1 / (perSecond * PeakSeconds));
        _sinceHit++;

        // The candidate is a few hops back, so that what followed it is known.
        var candidate = _rises.Count - 1 - Around;
        if (candidate < Around)
        {
            return 0;
        }

        var x = _rises[candidate];
        for (var d = -Around; d <= Around; d++)
        {
            if (_rises[candidate + d] > x || (d < 0 && _rises[candidate + d] == x))
            {
                return 0;
            }
        }

        var before = _rises.GetRange(0, candidate);
        before.Sort();
        var threshold = before[before.Count / 2] + MarginDb;
        var peak = _peak;
        _peak = Math.Max(_peak, x);
        if (x <= threshold || x <= OfPeak * peak || _sinceHit <= GapSeconds * perSecond)
        {
            return 0;
        }

        _sinceHit = 0;
        return peak > 0 ? Math.Clamp(x / peak, 0.6f, 1f) : 1f;
    }

    /// <summary>The mean rise in dB of the bands against two hops ago.</summary>
    private float Rise()
    {
        var size = _window.Length;
        for (var i = 0; i < size; i++)
        {
            var sum = 0;
            for (var c = 0; c < _channels; c++)
            {
                sum += _audio[i * _channels + c];
            }

            _re[i] = sum / (float)_channels / 32768f * _window[i];
            _im[i] = 0;
        }

        _fft.Transform(_re, _im);

        var oldest = _db[2];
        _db[2] = _db[1];
        _db[1] = _db[0];
        _db[0] = oldest;
        var now = _db[0];
        var loudest = QuietTopDb;
        var scale = 4f / size; // a full-scale sine reads about 0 dB
        for (var b = 0; b < Bands; b++)
        {
            var (from, to) = _bins[b];
            var power = 0f;
            for (var k = from; k < to; k++)
            {
                power += _re[k] * _re[k] + _im[k] * _im[k];
            }

            now[b] = 10 * MathF.Log10(power / (to - from) * scale * scale + 1e-12f);
            loudest = Math.Max(loudest, now[b]);
        }

        _top = Math.Max(_top - SinkDbPerHop, loudest);
        var floor = _top - RangeDb;
        var rise = 0f;
        for (var b = 0; b < Bands; b++)
        {
            now[b] = Math.Max(now[b], floor);
            rise += Math.Max(0, now[b] - _db[2][b]);
        }

        _hops = Math.Min(3, _hops + 1);
        return _hops == 3 ? rise / Bands : 0;
    }
}
