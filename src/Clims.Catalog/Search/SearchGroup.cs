namespace Clims.Catalog;

/// <summary>One group of search hits, best first, with the number of matches before the list was cut.</summary>
/// <typeparam name="T">Hit type.</typeparam>
/// <param name="Items">The best hits (at most the limit that was asked for).</param>
/// <param name="Total">How many items matched in all.</param>
public sealed record SearchGroup<T>(IReadOnlyList<T> Items, int Total)
{
    /// <summary>A group with no hits.</summary>
    public static SearchGroup<T> None { get; } = new(Array.Empty<T>(), 0);
}
