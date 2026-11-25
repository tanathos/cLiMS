namespace Clims.Visualizer;

/// <summary>Rebuilds the source ring of samples from full and delta frames, the way the vismux destination does.</summary>
public sealed class SampleRing
{
    private short[] _buffer = [];

    /// <summary>Applies a frame: a full frame replaces the ring, a delta is written so that it ends at the frame's index.</summary>
    public void Apply(VismuxFrame frame)
    {
        var size = (int)frame.BufSize;
        if (_buffer.Length != size)
        {
            _buffer = new short[size];
        }

        if (frame.IsFull)
        {
            frame.Samples.CopyTo(_buffer, 0);
            return;
        }

        var start = ((int)frame.BufIndex - frame.Samples.Length + size) % size;
        for (var i = 0; i < frame.Samples.Length; i++)
        {
            _buffer[(start + i) % size] = frame.Samples[i];
        }
    }

    /// <summary>The largest absolute sample among the newest <paramref name="count"/> samples before <paramref name="bufIndex"/>.</summary>
    public int Peak(uint bufIndex, int count)
    {
        var size = _buffer.Length;
        if (size == 0)
        {
            return 0;
        }

        var peak = 0;
        for (var i = 1; i <= Math.Min(count, size); i++)
        {
            peak = Math.Max(peak, Math.Abs((int)_buffer[Index(bufIndex, i)]));
        }

        return peak;
    }

    /// <summary>
    /// Copies the <paramref name="dest"/>.Length samples that end at <paramref name="bufIndex"/>, oldest first. False while the ring
    /// is still empty or smaller than that.
    /// </summary>
    public bool CopyLatest(uint bufIndex, short[] dest)
    {
        var size = _buffer.Length;
        if (size == 0 || dest.Length > size)
        {
            return false;
        }

        for (var i = 0; i < dest.Length; i++)
        {
            dest[i] = _buffer[Index(bufIndex, dest.Length - i)];
        }

        return true;
    }

    private int Index(uint bufIndex, int back) => (((int)bufIndex - back) % _buffer.Length + _buffer.Length) % _buffer.Length;
}
