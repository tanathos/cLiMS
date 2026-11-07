using System.Globalization;
using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

/// <summary>Shuffle setting.</summary>
public enum ShuffleMode
{
    /// <summary>In order.</summary>
    Off = 0,

    /// <summary>Shuffle tracks.</summary>
    Tracks = 1,

    /// <summary>Shuffle albums.</summary>
    Albums = 2,
}
