using System.Text.Json;

namespace Clims.Tui.Data;

/// <summary>A named queue kept on this PC.</summary>
/// <param name="Name">Name, unique ignoring case.</param>
/// <param name="SavedAt">When it was saved.</param>
/// <param name="Tracks">The tracks in order.</param>
public sealed record SavedQueue(string Name, DateTimeOffset SavedAt, IReadOnlyList<SavedTrack> Tracks);
