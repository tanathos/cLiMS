using System.Globalization;
using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

/// <summary>Play state of a player.</summary>
public enum PlayMode
{
    /// <summary>Stopped.</summary>
    Stop,

    /// <summary>Playing.</summary>
    Play,

    /// <summary>Paused.</summary>
    Pause,
}
