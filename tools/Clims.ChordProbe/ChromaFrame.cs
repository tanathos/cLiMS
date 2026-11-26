namespace Clims.ChordProbe;

/// <summary>The 12 pitch classes (from C) of one slice of time, for the middle register and for the bass.</summary>
/// <param name="Treble">C3 to B5, length 1 (all zero when there is nothing).</param>
/// <param name="Bass">E1 to D#3, the strongest class is 1.</param>
/// <param name="Level">RMS of the slice in dBFS.</param>
internal sealed record ChromaFrame(float[] Treble, float[] Bass, float Level);
