namespace Clims.Info;

/// <summary>A web address about an artist or an album.</summary>
/// <param name="Kind">What it is, in the provider's words ("official homepage", "wikipedia", "bandcamp"…).</param>
/// <param name="Url">The address.</param>
public sealed record InfoLink(string Kind, string Url);
