namespace Clims.Waveform;

/// <summary>In-place radix-2 FFT with a precomputed twiddle table, for one fixed power-of-two size.</summary>
internal sealed class Fft
{
    private readonly int _n;
    private readonly float[] _cos;
    private readonly float[] _sin;
    private readonly int[] _reverse;

    public Fft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0)
        {
            throw new ArgumentException("The size must be a power of two.", nameof(size));
        }

        _n = size;
        _cos = new float[size / 2];
        _sin = new float[size / 2];
        for (var i = 0; i < size / 2; i++)
        {
            _cos[i] = (float)Math.Cos(-2 * Math.PI * i / size);
            _sin[i] = (float)Math.Sin(-2 * Math.PI * i / size);
        }

        _reverse = new int[size];
        var bits = (int)Math.Log2(size);
        for (var i = 0; i < size; i++)
        {
            var r = 0;
            for (var b = 0; b < bits; b++)
            {
                r |= ((i >> b) & 1) << (bits - 1 - b);
            }

            _reverse[i] = r;
        }
    }

    public int Size => _n;

    /// <summary>Transforms <paramref name="re"/> and <paramref name="im"/> (both of length <see cref="Size"/>) in place.</summary>
    public void Transform(float[] re, float[] im)
    {
        for (var i = 0; i < _n; i++)
        {
            var j = _reverse[i];
            if (j > i)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var len = 2; len <= _n; len <<= 1)
        {
            var half = len / 2;
            var step = _n / len;
            for (var start = 0; start < _n; start += len)
            {
                for (var k = 0; k < half; k++)
                {
                    var wr = _cos[k * step];
                    var wi = _sin[k * step];
                    var a = start + k;
                    var b = a + half;
                    var tr = re[b] * wr - im[b] * wi;
                    var ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
            }
        }
    }
}
