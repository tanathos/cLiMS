using Clims.Lms;

namespace Clims.Catalog;

/// <summary>Settings for <see cref="SearchService"/>.</summary>
public sealed class SearchServiceOptions
{
    /// <summary>Wait after the last keystroke before the server is asked (the local result needs no wait).</summary>
    public TimeSpan ServerDebounce { get; init; } = TimeSpan.FromMilliseconds(120);

    /// <summary>Hits requested per group from the server.</summary>
    public int ServerCount { get; init; } = 50;
}
