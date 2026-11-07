using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

/// <summary>One page of a larger result set. <see cref="Total"/> is the server's full count, not the page size.</summary>
/// <typeparam name="T">Item type.</typeparam>
/// <param name="Items">Items in this page (empty when the server returned no loop).</param>
/// <param name="Total">Total number of matching items on the server.</param>
/// <param name="Start">Zero-based index of the first item.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int Start)
{
    /// <summary>True when more items exist after this page.</summary>
    public bool HasMore => Start + Items.Count < Total;
}
