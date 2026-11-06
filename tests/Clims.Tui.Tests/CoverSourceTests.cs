using System.Net;
using Clims.Tui.Ui;
using SkiaSharp;

namespace Clims.Tui.Tests;

public class CoverSourceTests
{
    private static readonly Uri Base = new("http://lms.test:9000/");

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(4, 4);
        bitmap.Erase(new SKColor(200, 30, 30));
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<string, CancellationToken, Task<HttpResponseMessage>> _respond;

        public Handler(Func<string, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        public List<string> Requested { get; } = new();

        public int Active;

        public int PeakActive;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var id = request.RequestUri!.Segments[2].TrimEnd('/');
            lock (Requested)
            {
                Requested.Add(id);
            }

            var now = Interlocked.Increment(ref Active);
            int seen;
            while (now > (seen = Volatile.Read(ref PeakActive)) && Interlocked.CompareExchange(ref PeakActive, now, seen) != seen)
            {
            }

            try
            {
                return await _respond(id, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref Active);
            }
        }
    }

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Png()) };

    private static async Task Eventually(Func<bool> condition, int milliseconds = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "condition not met in time");
            await Task.Delay(10);
        }
    }

    private static CoverKey Key(string id) => new(id, 0);

    [Fact]
    public async Task A_wanted_cover_is_fetched_decoded_cached_and_announced()
    {
        var handler = new Handler((_, _) => Task.FromResult(Ok()));
        using var source = new LmsCoverSource(new HttpClient(handler), Base);
        var updates = 0;
        source.Updated += () => Interlocked.Increment(ref updates);
        var owner = new object();

        Assert.Null(source.TryGet(Key("a1")));
        source.Want(owner, new[] { Key("a1") });
        await Eventually(() => source.TryGet(Key("a1")) is not null);

        Assert.Equal(1, updates);
        Assert.Equal(new[] { "a1" }, handler.Requested);
        source.Want(owner, new[] { Key("a1") }); // already cached: no second request
        await Task.Delay(50);
        Assert.Single(handler.Requested);
    }

    [Fact]
    public async Task The_request_asks_for_the_artwork_id_at_a_useful_size()
    {
        Uri? seen = null;
        var inner = new Handler((_, _) => Task.FromResult(Ok()));
        var client = new HttpClient(new CapturingHandler(inner, u => seen = u));
        using var source = new LmsCoverSource(client, Base);
        source.Want(new object(), new[] { Key("2836a7e5") });
        await Eventually(() => seen is not null);

        Assert.Equal("http://lms.test:9000/music/2836a7e5/cover_300x300_o.jpg", seen!.ToString());
    }

    private sealed class CapturingHandler : DelegatingHandler
    {
        private readonly Action<Uri> _capture;

        public CapturingHandler(HttpMessageHandler inner, Action<Uri> capture)
            : base(inner)
        {
            _capture = capture;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _capture(request.RequestUri!);
            return base.SendAsync(request, cancellationToken);
        }
    }

    [Fact]
    public async Task Albums_without_artwork_are_never_requested()
    {
        var handler = new Handler((_, _) => Task.FromResult(Ok()));
        using var source = new LmsCoverSource(new HttpClient(handler), Base);
        source.Want(new object(), new[] { new CoverKey(null, 7) });
        await Task.Delay(100);

        Assert.Empty(handler.Requested);
        Assert.Null(source.TryGet(new CoverKey(null, 7)));
    }

    [Fact]
    public async Task Requests_are_limited_to_three_at_a_time()
    {
        var gate = new TaskCompletionSource();
        var handler = new Handler(async (_, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return Ok();
        });
        using var source = new LmsCoverSource(new HttpClient(handler), Base, concurrency: 3);
        source.Want(new object(), Enumerable.Range(0, 10).Select(i => Key("c" + i)).ToArray());

        await Eventually(() => Volatile.Read(ref handler.Active) == 3);
        await Task.Delay(100);
        Assert.Equal(3, handler.PeakActive);

        gate.SetResult();
        await Eventually(() => Enumerable.Range(0, 10).All(i => source.TryGet(Key("c" + i)) is not null));
        Assert.Equal(3, handler.PeakActive);
    }

    [Fact]
    public async Task Scrolling_away_cancels_covers_that_have_not_started()
    {
        var gate = new TaskCompletionSource();
        var handler = new Handler(async (_, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return Ok();
        });
        using var source = new LmsCoverSource(new HttpClient(handler), Base, concurrency: 1);
        var owner = new object();
        source.Want(owner, new[] { Key("a"), Key("b"), Key("c") });
        await Eventually(() => handler.Requested.Count == 1); // "a" holds the only slot; "b" and "c" wait

        source.Want(owner, new[] { Key("c") });               // the view scrolled: only "c" is still on screen
        gate.SetResult();
        await Eventually(() => source.TryGet(Key("c")) is not null);

        Assert.DoesNotContain("b", handler.Requested);
        Assert.Null(source.TryGet(Key("b")));
    }

    [Fact]
    public async Task A_cover_two_views_want_is_fetched_once_and_survives_one_of_them_leaving()
    {
        var gate = new TaskCompletionSource();
        var handler = new Handler(async (_, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return Ok();
        });
        using var source = new LmsCoverSource(new HttpClient(handler), Base);
        var a = new object();
        var b = new object();
        source.Want(a, new[] { Key("shared") });
        source.Want(b, new[] { Key("shared") });
        source.Want(a, Array.Empty<CoverKey>());   // one view lets go; the other still wants it
        gate.SetResult();

        await Eventually(() => source.TryGet(Key("shared")) is not null);
        Assert.Single(handler.Requested);
    }

    [Fact]
    public async Task Failures_are_not_hammered_and_leave_no_image()
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var source = new LmsCoverSource(new HttpClient(handler), Base);
        var owner = new object();
        source.Want(owner, new[] { Key("gone") });
        await Eventually(() => handler.Requested.Count == 1);
        await Task.Delay(100);

        source.Want(owner, new[] { Key("gone") });
        source.Want(owner, new[] { Key("gone") });
        await Task.Delay(100);

        Assert.Single(handler.Requested); // backed off, not retried on every repaint
        Assert.Null(source.TryGet(Key("gone")));
    }

    [Fact]
    public async Task Something_that_is_not_an_image_counts_as_a_failure()
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>nope</html>") }));
        using var source = new LmsCoverSource(new HttpClient(handler), Base);
        var owner = new object();
        source.Want(owner, new[] { Key("junk") });
        await Eventually(() => handler.Requested.Count == 1);
        await Task.Delay(100);

        source.Want(owner, new[] { Key("junk") });
        source.Want(owner, new[] { Key("junk") });
        await Task.Delay(100);

        Assert.Null(source.TryGet(Key("junk")));
        Assert.Single(handler.Requested); // recorded as a failure (backed off), not refetched on every repaint
    }

    [Fact]
    public async Task The_cache_is_a_least_recently_used_list_of_bounded_size()
    {
        var handler = new Handler((_, _) => Task.FromResult(Ok()));
        using var source = new LmsCoverSource(new HttpClient(handler), Base, capacity: 10);
        var owner = new object();
        for (var i = 0; i < 12; i++)
        {
            source.Want(owner, new[] { Key("k" + i) });
            await Eventually(() => source.TryGet(Key("k" + i)) is not null);
            if (i >= 1)
            {
                _ = source.TryGet(Key("k0")); // keep k0 recently used
            }
        }

        Assert.NotNull(source.TryGet(Key("k0")));   // touched, so kept
        Assert.Null(source.TryGet(Key("k1")));      // the least recently used went first
        Assert.NotNull(source.TryGet(Key("k11")));
    }

    [Fact]
    public void Generated_covers_work_without_a_server()
    {
        var source = new ProceduralCoverSource();
        var a = source.TryGet(new CoverKey(null, 1));
        Assert.NotNull(a);
        Assert.Same(a, source.TryGet(new CoverKey(null, 1)));
        Assert.NotSame(a, source.TryGet(new CoverKey(null, 2)));
    }

    [Fact]
    public void An_image_resamples_to_the_cell_grid_and_gives_pixels()
    {
        using var image = CoverImage.Decode(Png(), "t")!;
        var p = image.Pixel(8, 8, 3, 3);
        Assert.True(p.R > 150 && p.G < 80 && p.B < 80);
        Assert.Equal(8, image.Bitmap(8, 6).GetLength(0));
        Assert.Equal(6, image.Bitmap(8, 6).GetLength(1));
    }

    [Fact]
    public void Garbage_does_not_decode() => Assert.Null(CoverImage.Decode(new byte[] { 1, 2, 3, 4 }, "x"));
}
