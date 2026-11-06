using System.Runtime.CompilerServices;

namespace Clims.Lms;

/// <summary>Helpers to walk a result set page by page.</summary>
public static class Paging
{
    /// <summary>
    /// Streams every item of a paged query. Stops when a page comes back empty or the server's total is reached, so a
    /// library that changes while paging cannot loop forever.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="fetch">Loads one page: (start, count, token).</param>
    /// <param name="pageSize">Items per request; 2 000 is a good size for tracks.</param>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    public static async IAsyncEnumerable<T> EnumerateAsync<T>(
        Func<int, int, CancellationToken, Task<Page<T>>> fetch,
        int pageSize = 500,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        var start = 0;
        while (true)
        {
            var page = await fetch(start, pageSize, cancellationToken).ConfigureAwait(false);
            foreach (var item in page.Items)
            {
                yield return item;
            }

            start += page.Items.Count;
            if (page.Items.Count == 0 || start >= page.Total)
            {
                yield break;
            }
        }
    }
}
