using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

/// <summary>A server-side saved playlist.</summary>
/// <param name="Id">Playlist id.</param>
/// <param name="Name">Playlist name.</param>
public sealed record SavedPlaylist(int Id, string Name)
{
    internal static SavedPlaylist From(JsonElement e) =>
        new(e.GetInt("id"), e.GetStringOrNull("playlist") ?? e.GetStringOrNull("title") ?? string.Empty);
}
