using Clims.Catalog;
using Clims.Lms;
using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public class AlbumRefresherTests
{
    private sealed class FakeService : ICatalogService
    {
        public List<int> Refreshed { get; } = new();

        public int Active;

        public int Peak;

        /// <summary>When set, refreshes wait for it (and observe cancellation).</summary>
        public TaskCompletionSource? Gate { get; set; }

        public Exception? Failure { get; set; }

        public Catalog.Catalog? Current => null;

        public SyncStatus Status => SyncStatus.Initial;

        public event Action<SyncStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public event Action<Catalog.Catalog?>? CatalogChanged
        {
            add { }
            remove { }
        }

        public event Action<SyncPrompt>? PromptRequested
        {
            add { }
            remove { }
        }

        public event Action<int>? AlbumRefreshed
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CheckServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SyncAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void DeferSync()
        {
        }

        public void CancelSync()
        {
        }

        public Task RunEventsAsync(ILmsEventStream events, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
        }

        public async Task<bool> RefreshAlbumAsync(int albumId, CancellationToken cancellationToken = default)
        {
            var now = Interlocked.Increment(ref Active);
            int seen;
            while (now > (seen = Volatile.Read(ref Peak)) && Interlocked.CompareExchange(ref Peak, now, seen) != seen)
            {
            }

            try
            {
                lock (Refreshed)
                {
                    Refreshed.Add(albumId);
                }

                if (Gate is { } gate)
                {
                    await gate.Task.WaitAsync(cancellationToken);
                }

                if (Failure is not null)
                {
                    throw Failure;
                }

                return true;
            }
            finally
            {
                Interlocked.Decrement(ref Active);
            }
        }
    }

    private static CatalogAlbum Album(int id) => new(id, "A" + id, 1, "Artist", 2000, null, false, 1);

    private static IReadOnlyList<CatalogAlbum> Albums(params int[] ids) => ids.Select(Album).ToList();

    private static async Task Eventually(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "condition not met in time");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Visible_albums_are_refreshed_once_each()
    {
        var service = new FakeService();
        using var refresher = new AlbumRefresher(service);

        refresher.SetVisible(Albums(1, 2, 3));
        await Eventually(() => service.Refreshed.Count == 3);
        refresher.SetVisible(Albums(1, 2, 3));   // the same albums again (a repaint, a resize)
        refresher.SetVisible(Albums(2, 3, 4));   // scrolled by one
        await Eventually(() => service.Refreshed.Count == 4);
        await Task.Delay(50);

        Assert.Equal(new[] { 1, 2, 3, 4 }, service.Refreshed.Order());
    }

    [Fact]
    public async Task At_most_two_refreshes_run_at_once()
    {
        var service = new FakeService { Gate = new TaskCompletionSource() };
        using var refresher = new AlbumRefresher(service, concurrency: 2);

        refresher.SetVisible(Albums(1, 2, 3, 4, 5, 6));
        await Eventually(() => Volatile.Read(ref service.Active) == 2);
        await Task.Delay(100);
        Assert.Equal(2, service.Peak);

        service.Gate.SetResult();
        await Eventually(() => service.Refreshed.Count == 6);
        Assert.Equal(2, service.Peak);
    }

    [Fact]
    public async Task Albums_scrolled_away_before_they_start_are_skipped_and_retried_when_shown_again()
    {
        var service = new FakeService { Gate = new TaskCompletionSource() };
        using var refresher = new AlbumRefresher(service, concurrency: 1);

        refresher.SetVisible(Albums(1, 2, 3));
        await Eventually(() => service.Refreshed.Count == 1);   // album 1 is running, 2 and 3 wait for the slot
        refresher.SetVisible(Albums(3));                         // scrolled: only 3 is still visible
        service.Gate.SetResult();
        await Eventually(() => service.Refreshed.Contains(3));
        await Task.Delay(100);

        Assert.DoesNotContain(2, service.Refreshed);             // never fetched: it had left the screen

        refresher.SetVisible(Albums(2));                         // back on screen: fetched now
        await Eventually(() => service.Refreshed.Contains(2));
    }

    [Fact]
    public async Task Reset_lets_albums_refresh_again_after_a_new_catalogue()
    {
        var service = new FakeService();
        using var refresher = new AlbumRefresher(service);
        refresher.SetVisible(Albums(1));
        await Eventually(() => service.Refreshed.Count == 1);
        await Task.Delay(50);

        refresher.Reset();
        refresher.SetVisible(Albums(2));
        refresher.SetVisible(Albums(1));
        await Eventually(() => service.Refreshed.Count(i => i == 1) == 2);
    }

    [Fact]
    public async Task A_failing_refresh_is_swallowed_and_not_retried_in_a_loop()
    {
        var service = new FakeService { Failure = new InvalidOperationException("boom") };
        using var refresher = new AlbumRefresher(service);

        refresher.SetVisible(Albums(1));
        await Eventually(() => service.Refreshed.Count == 1);
        await Task.Delay(100);
        refresher.SetVisible(Albums(1));
        await Task.Delay(100);

        Assert.Single(service.Refreshed);
    }

    [Fact]
    public async Task An_empty_visible_set_cancels_what_is_waiting()
    {
        var service = new FakeService { Gate = new TaskCompletionSource() };
        using var refresher = new AlbumRefresher(service, concurrency: 1);
        refresher.SetVisible(Albums(1, 2));
        await Eventually(() => service.Refreshed.Count == 1);

        refresher.SetVisible(Array.Empty<CatalogAlbum>());       // the Library tab was hidden
        await Task.Delay(100);

        Assert.Equal(new[] { 1 }, service.Refreshed);
        Assert.Equal(0, Volatile.Read(ref service.Active));      // the running one was cancelled too
    }
}
