using Clims.Lms;

namespace Clims.Waveform;

/// <summary>Settings for <see cref="WaveformService"/>.</summary>
public sealed class WaveformOptions
{
    /// <summary>Peaks per track.</summary>
    public int Points { get; init; } = PeakAnalyzer.DefaultPoints;

    /// <summary>Profiles kept in memory (about 5 KB each).</summary>
    public int CacheSize { get; init; } = 64;

    /// <summary>Largest file that is downloaded for analysis.</summary>
    public long MaxBytes { get; init; } = 400L * 1024 * 1024;

    /// <summary>How long a failed track is left alone before it can be tried again.</summary>
    public TimeSpan RetryAfter { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Folder for the temporary download; the system temp folder by default.</summary>
    public string? TempFolder { get; init; }

    /// <summary>Clock, replaceable in tests.</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;
}
