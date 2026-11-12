namespace Clims.Lms;

/// <summary>What to do with something playable from a plugin.</summary>
public enum OnlineAction
{
    /// <summary>Replace the queue and play it.</summary>
    Play,

    /// <summary>Add it to the end of the queue.</summary>
    Add,

    /// <summary>Put it right after the current track.</summary>
    Insert,
}
