namespace Clims.Lms;

/// <summary>A page of one level of a plugin's browse tree.</summary>
/// <param name="Title">The name of the level (an album, "Search"…); empty when the plugin gave none.</param>
/// <param name="Items">The lines of this page.</param>
/// <param name="Total">The number of lines on the server.</param>
/// <param name="Start">Index of the first line of this page.</param>
public sealed record OnlineListing(string Title, IReadOnlyList<OnlineItem> Items, int Total, int Start)
{
    /// <summary>True when more lines follow this page.</summary>
    public bool HasMore => Start + Items.Count < Total;
}
