using System.Threading.Channels;
using Clims.Lms;
using Clims.Tui.Ui;

namespace Clims.Tui.Data;

/// <summary>Settings for <see cref="LmsPlayerController"/>.</summary>
public sealed class LmsPlayerOptions
{
    /// <summary>Player to control (its id, usually the MAC address). Null: the first connected hardware player.</summary>
    public string? PlayerId { get; init; }

    /// <summary>How often the server is asked for the player status between events.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How long to wait before looking for the player or the server again after a failure.</summary>
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Most queue entries read from the server (longer queues show their first part).</summary>
    public int MaxQueue { get; init; } = 500;

    /// <summary>A "previous" press later than this into a track restarts the track instead of going back.</summary>
    public TimeSpan RestartThreshold { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Clock, replaceable in tests.</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;
}
