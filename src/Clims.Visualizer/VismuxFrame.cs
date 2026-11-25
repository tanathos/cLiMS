namespace Clims.Visualizer;

/// <summary>One decoded vismux DATA datagram.</summary>
/// <param name="Sequence">Per-source packet counter.</param>
/// <param name="BufSize">Ring size in samples (16384).</param>
/// <param name="BufIndex">Write position of the source, exclusive end of the newest sample.</param>
/// <param name="Rate">Sample rate in Hz.</param>
/// <param name="Running">False while the player is paused or stopped.</param>
/// <param name="Samples">The payload, converted to host byte order.</param>
public sealed record VismuxFrame(uint Sequence, uint BufSize, uint BufIndex, uint Rate, bool Running, short[] Samples)
{
    /// <summary>A full frame carries the whole ring; anything shorter is a delta ending at <see cref="BufIndex"/>.</summary>
    public bool IsFull => Samples.Length == BufSize;
}
