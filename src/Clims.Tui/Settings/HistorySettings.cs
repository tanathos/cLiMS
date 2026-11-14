namespace Clims.Tui.Settings;

/// <summary>The "History" node: the Played tab.</summary>
public sealed record HistorySettings
{
    /// <summary>How many days back the track list of the Played tab goes. The album lists always cover 7 and 30 days.</summary>
    public int TrackDays { get; init; } = 3;

    /// <summary>How much of an album (50, 75 or 100 percent of its tracks) must be played for it to count as listened to.</summary>
    public int ListenPercent { get; init; } = 50;
}
