using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

/// <summary>A genre.</summary>
/// <param name="Id">LMS genre id.</param>
/// <param name="Name">Genre name.</param>
/// <param name="TextKey">First-letter key, when sent.</param>
public sealed record Genre(int Id, string Name, string? TextKey)
{
    internal static Genre From(JsonElement e) =>
        new(e.GetInt("id"), e.GetStringOrNull("genre") ?? e.GetStringOrNull("title") ?? string.Empty, e.GetStringOrNull("textkey"));
}
