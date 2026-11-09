namespace Clims.Waveform;

/// <summary>
/// The spectrum of one track over time, precomputed from the file: a few dozen frequency bands (low to high, log spaced),
/// a fixed number of frames per second, each level one byte (0 silent, 255 loudest). Frame <c>i</c> is centred on second
/// <c>i / FramesPerSecond</c>.
/// </summary>
/// <param name="Levels">Frame after frame, <paramref name="Bands"/> bytes each.</param>
/// <param name="Bands">Bands per frame.</param>
/// <param name="FramesPerSecond">Frames per second of audio.</param>
public sealed record SpectrumProfile(byte[] Levels, int Bands, int FramesPerSecond)
{
    /// <summary>Number of frames.</summary>
    public int FrameCount => Bands == 0 ? 0 : Levels.Length / Bands;

    /// <summary>Index of the frame playing at <paramref name="elapsed"/> (clamped to the track), or -1 for a track with no frames.</summary>
    public int IndexAt(TimeSpan elapsed) =>
        FrameCount == 0 ? -1 : Math.Clamp((int)Math.Round(elapsed.TotalSeconds * FramesPerSecond), 0, FrameCount - 1);

    /// <summary>The levels of one frame.</summary>
    public ReadOnlySpan<byte> Frame(int index) => Levels.AsSpan(index * Bands, Bands);

    /// <summary>The frame playing at <paramref name="elapsed"/>, or an empty span for a track with no frames.</summary>
    public ReadOnlySpan<byte> At(TimeSpan elapsed) => IndexAt(elapsed) is var i and >= 0 ? Frame(i) : ReadOnlySpan<byte>.Empty;
}
