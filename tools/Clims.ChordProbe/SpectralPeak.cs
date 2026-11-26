namespace Clims.ChordProbe;

/// <summary>A spectral peak: its pitch as a fractional MIDI number and its magnitude.</summary>
internal readonly record struct SpectralPeak(double Midi, float Magnitude);
