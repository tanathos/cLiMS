using NAudio.Flac;
using NAudio.Wave;
using NVorbis;

namespace Clims.Waveform;

/// <summary>An overview of one track: loudness per slice of its length, 0 to 1.</summary>
/// <param name="Peaks">Peak amplitude of each equal slice of the track, normalised so the loudest slice is 1.</param>
/// <param name="Duration">Length of the decoded audio.</param>
public sealed record PeakProfile(float[] Peaks, TimeSpan Duration);
