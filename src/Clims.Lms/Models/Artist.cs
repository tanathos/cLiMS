using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

/// <summary>An artist. <see cref="TextKey"/> is the A-Z jump key LMS computes (first letter, digits and symbols grouped).</summary>
/// <param name="Id">LMS artist id; may change after a full rescan.</param>
/// <param name="Name">Display name.</param>
/// <param name="TextKey">First-letter key, when the server sent it.</param>
public sealed record Artist(int Id, string Name, string? TextKey)
{
    internal static Artist From(JsonElement e) =>
        new(e.GetInt("id"), e.GetStringOrNull("artist") ?? e.GetStringOrNull("title") ?? string.Empty, e.GetStringOrNull("textkey"));
}
