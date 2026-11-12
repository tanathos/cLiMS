using Clims.Lms.Json;
using System.Text.Json;

namespace Clims.Lms;

/// <summary>One line of a plugin's browse tree: a folder, a search box, something playable, or plain text.</summary>
/// <param name="Id">The id to browse into or play (<c>2.0.1</c>): opaque, the plugin makes it up.</param>
/// <param name="Name">The text to show.</param>
/// <param name="Type">The plugin's own type (<c>link</c>, <c>playlist</c>, <c>audio</c>, <c>search</c>, <c>text</c>…).</param>
/// <param name="IsAudio">True when it can be played or queued.</param>
/// <param name="HasItems">True when it can be opened.</param>
public sealed record OnlineItem(string Id, string Name, string Type, bool IsAudio, bool HasItems)
{
    /// <summary>True for a search box: opening it needs a text.</summary>
    public bool IsSearch => string.Equals(Type, "search", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for a line that only informs (a message such as "please log in"): nothing to open or play.</summary>
    public bool IsText => !IsAudio && !IsSearch && (Type is "text" or "textarea" || !HasItems);

    internal static OnlineItem From(JsonElement e) => new(
        e.GetStringOrNull("id") ?? string.Empty,
        e.GetStringOrNull("name") ?? e.GetStringOrNull("title") ?? string.Empty,
        e.GetStringOrNull("type") ?? string.Empty,
        e.GetBool("isaudio"),
        e.GetBool("hasitems"));
}
