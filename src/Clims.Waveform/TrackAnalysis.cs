namespace Clims.Waveform;

/// <summary>Everything one decode of a track yields.</summary>
/// <param name="Peaks">The overview waveform.</param>
/// <param name="Spectrum">The spectrum over time.</param>
public sealed record TrackAnalysis(PeakProfile Peaks, SpectrumProfile Spectrum);
