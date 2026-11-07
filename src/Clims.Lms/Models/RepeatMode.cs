using System.Globalization;
using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

/// <summary>Repeat setting.</summary>
public enum RepeatMode
{
    /// <summary>No repeat.</summary>
    Off = 0,

    /// <summary>Repeat the current track.</summary>
    Track = 1,

    /// <summary>Repeat the whole queue.</summary>
    Queue = 2,
}
