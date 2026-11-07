using Clims.Lms;

namespace Clims.Waveform;

/// <summary>Where a track's waveform stands.</summary>
public enum WaveformState
{
    /// <summary>Nobody asked for it yet.</summary>
    Unknown,

    /// <summary>Downloading or decoding.</summary>
    Loading,

    /// <summary>Peaks are available.</summary>
    Ready,

    /// <summary>The format cannot be decoded (shown as "no waveform", never retried).</summary>
    Unsupported,

    /// <summary>Download or decode failed (retried after a pause).</summary>
    Failed,
}
