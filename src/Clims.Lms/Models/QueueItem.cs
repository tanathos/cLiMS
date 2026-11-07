using System.Globalization;
using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

/// <summary>A track in a player's queue.</summary>
/// <param name="Index">Zero-based position in the queue.</param>
/// <param name="Track">The track.</param>
public sealed record QueueItem(int Index, Track Track);
