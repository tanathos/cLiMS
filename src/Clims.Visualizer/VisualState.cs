namespace Clims.Visualizer;

/// <summary>What a <see cref="IVisualSource"/> is getting from the player.</summary>
public enum VisualState
{
    /// <summary>Nothing heard (yet) from the player: not configured, unreachable, or vismux is not running.</summary>
    Offline,

    /// <summary>The player answers but is paused or stopped.</summary>
    Paused,

    /// <summary>Samples of the audio being played are arriving.</summary>
    Live,
}
