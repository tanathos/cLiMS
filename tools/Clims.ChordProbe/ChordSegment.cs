namespace Clims.ChordProbe;

/// <summary>A stretch of time with one chord.</summary>
internal sealed record ChordSegment(double Start, double End, string Name);
