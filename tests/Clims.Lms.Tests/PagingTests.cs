using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms.Tests;

public class PagingTests
{
    private static Func<int, int, CancellationToken, Task<Page<int>>> Source(int total, List<(int Start, int Count)> calls) =>
        (start, count, _) =>
        {
            calls.Add((start, count));
            var items = Enumerable.Range(start, Math.Max(0, Math.Min(count, total - start))).ToList();
            return Task.FromResult(new Page<int>(items, total, start));
        };

    [Fact]
    public async Task Walks_every_page_in_order()
    {
        var calls = new List<(int, int)>();
        var all = new List<int>();
        await foreach (var i in Paging.EnumerateAsync(Source(25, calls), 10))
        {
            all.Add(i);
        }

        Assert.Equal(Enumerable.Range(0, 25), all);
        Assert.Equal(new[] { (0, 10), (10, 10), (20, 10) }, calls);
    }

    [Fact]
    public async Task An_exact_multiple_does_not_ask_for_an_extra_page()
    {
        var calls = new List<(int, int)>();
        await foreach (var _ in Paging.EnumerateAsync(Source(20, calls), 10))
        {
        }

        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public async Task Stops_on_an_empty_page_even_if_the_total_is_larger()
    {
        // A library that shrinks while paging must not loop forever.
        var n = 0;
        Task<Page<int>> Fetch(int start, int count, CancellationToken ct) =>
            Task.FromResult(n++ == 0 ? new Page<int>(new[] { 1, 2 }, 100, start) : new Page<int>(Array.Empty<int>(), 100, start));

        var all = new List<int>();
        await foreach (var i in Paging.EnumerateAsync(Fetch, 2))
        {
            all.Add(i);
        }

        Assert.Equal(new[] { 1, 2 }, all);
    }

    [Fact]
    public async Task Empty_result_yields_nothing()
    {
        var calls = new List<(int, int)>();
        await foreach (var _ in Paging.EnumerateAsync(Source(0, calls), 10))
        {
            Assert.Fail("no items expected");
        }

        Assert.Single(calls);
    }

    [Fact]
    public async Task Cancellation_stops_the_enumeration()
    {
        using var cts = new CancellationTokenSource();
        var calls = new List<(int, int)>();
        var seen = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in Paging.EnumerateAsync(
                               async (s, c, t) =>
                               {
                                   t.ThrowIfCancellationRequested();
                                   return await Source(1000, calls)(s, c, t);
                               },
                               10,
                               cts.Token))
            {
                if (++seen == 15)
                {
                    cts.Cancel();
                }
            }
        });
        Assert.True(seen < 1000);
    }

    [Fact]
    public void Page_knows_whether_more_follow()
    {
        Assert.True(new Page<int>(new[] { 1, 2 }, 5, 0).HasMore);
        Assert.False(new Page<int>(new[] { 4, 5 }, 5, 3).HasMore);
    }
}
