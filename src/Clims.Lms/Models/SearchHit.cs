using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

/// <summary>One search result: just an id and a display name.</summary>
/// <param name="Id">Artist, album, track or genre id.</param>
/// <param name="Name">Display name.</param>
public sealed record SearchHit(int Id, string Name);
