using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Clims.Lms;
using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>
/// Supplies cover images to the views. <see cref="TryGet"/> never blocks: it returns what is cached and views ask for
/// the rest with <see cref="Want"/>, then repaint when <see cref="Updated"/> fires.
/// </summary>
public interface ICoverSource
{
    /// <summary>Raised (on any thread) when a requested cover became available.</summary>
    event Action? Updated;

    /// <summary>The cover if it is ready, otherwise null.</summary>
    CoverImage? TryGet(CoverKey key);

    /// <summary>
    /// Declares which covers an owner (a view) shows right now. Covers that nobody wants any more are cancelled if not
    /// yet fetched, so scrolling fast does not build up a queue of requests for albums that are gone.
    /// </summary>
    void Want(object owner, IReadOnlyCollection<CoverKey> keys);
}

/// <summary>Covers generated from the seed: for the demo mode (no server).</summary>
public sealed class ProceduralCoverSource : ICoverSource
{
    private readonly Dictionary<int, CoverImage> _cache = new();

    /// <inheritdoc />
    public event Action? Updated
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public CoverImage? TryGet(CoverKey key)
    {
        lock (_cache)
        {
            if (!_cache.TryGetValue(key.Seed, out var image))
            {
                _cache[key.Seed] = image = ProceduralCovers.Create(key.Seed);
            }

            return image;
        }
    }

    /// <inheritdoc />
    public void Want(object owner, IReadOnlyCollection<CoverKey> keys)
    {
    }
}

/// <summary>
/// Fetches covers from the server only when a view shows them: 2 to 3 requests at a time, cancelled when scrolled
/// away, kept in a memory-only LRU (no disk cache, as decided). The server resizes, so no local scaling is needed.
/// </summary>
public sealed class LmsCoverSource : ICoverSource, IDisposable
{
    private const int RequestSize = 300;
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly AuthenticationHeaderValue? _auth;
    private readonly SemaphoreSlim _slots;
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Id, CoverImage Image)>> _cache = new();
    private readonly LinkedList<(string Id, CoverImage Image)> _lru = new();
    private readonly Dictionary<string, CancellationTokenSource> _pending = new();
    private readonly Dictionary<string, DateTimeOffset> _failed = new();
    private readonly Dictionary<object, HashSet<string>> _wanted = new();

    /// <summary>Creates the source. The <see cref="HttpClient"/> is not disposed by it.</summary>
    public LmsCoverSource(HttpClient http, Uri baseUri, NetworkCredential? credentials = null, int capacity = 300, int concurrency = 3)
    {
        _http = http;
        _baseUri = baseUri;
        _capacity = Math.Max(10, capacity);
        _slots = new SemaphoreSlim(Math.Max(1, concurrency));
        if (credentials is not null)
        {
            _auth = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.UserName}:{credentials.Password}")));
        }
    }

    /// <inheritdoc />
    public event Action? Updated;

    /// <inheritdoc />
    public CoverImage? TryGet(CoverKey key)
    {
        if (key.ArtworkId is not { } id)
        {
            return null;
        }

        lock (_gate)
        {
            if (!_cache.TryGetValue(id, out var node))
            {
                return null;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value.Image;
        }
    }

    /// <inheritdoc />
    public void Want(object owner, IReadOnlyCollection<CoverKey> keys)
    {
        var ids = keys.Where(k => k.ArtworkId is not null).Select(k => k.ArtworkId!).ToHashSet();
        List<string> start;
        lock (_gate)
        {
            _wanted[owner] = ids;
            var all = _wanted.Values.SelectMany(s => s).ToHashSet();

            foreach (var (id, cts) in _pending.Where(p => !all.Contains(p.Key)).ToList())
            {
                cts.Cancel();
            }

            var now = DateTimeOffset.UtcNow;
            start = ids
                .Where(id => !_cache.ContainsKey(id) && !_pending.ContainsKey(id)
                             && !(_failed.TryGetValue(id, out var when) && now - when < FailureBackoff))
                .ToList();
            foreach (var id in start)
            {
                _pending[id] = new CancellationTokenSource();
            }
        }

        foreach (var id in start)
        {
            _ = FetchAsync(id);
        }
    }

    /// <summary>Cancels everything still in flight.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var cts in _pending.Values)
            {
                cts.Cancel();
            }
        }
    }

    private async Task FetchAsync(string id)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            cts = _pending[id];
        }

        var ct = cts.Token;
        var acquired = false;
        try
        {
            await _slots.WaitAsync(ct).ConfigureAwait(false);
            acquired = true;

            // A slot can free up just before this cover's cancellation arrives: check once more that someone still wants it.
            lock (_gate)
            {
                if (ct.IsCancellationRequested || !_wanted.Values.Any(s => s.Contains(id)))
                {
                    return;
                }
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, ArtworkUrl.Cover(_baseUri, id, RequestSize, RequestSize));
            request.Headers.Authorization = _auth;
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

            var image = CoverImage.Decode(bytes, id) ?? throw new InvalidDataException("Not an image.");
            lock (_gate)
            {
                _cache[id] = _lru.AddFirst((id, image));
                while (_lru.Count > _capacity)
                {
                    var last = _lru.Last!;
                    _lru.RemoveLast();
                    _cache.Remove(last.Value.Id);
                    last.Value.Image.Dispose();
                }
            }

            Updated?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Scrolled away before it was needed: forget it, it can be requested again later.
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException)
        {
            lock (_gate)
            {
                _failed[id] = DateTimeOffset.UtcNow;
            }
        }
        finally
        {
            if (acquired)
            {
                _slots.Release();
            }

            lock (_gate)
            {
                _pending.Remove(id);
            }

            cts.Dispose();
        }
    }
}

/// <summary>Generates fake covers (gradient plus a blob) from a seed.</summary>
public static class ProceduralCovers
{
    private sealed record Recipe(double Hue, double Hue2, double Cx, double Cy, double Rad, int Stripe);

    /// <summary>A 96x96 generated cover.</summary>
    public static CoverImage Create(int seed)
    {
        const int size = 96;
        var recipe = RecipeFor(seed);
        var pixels = new Color[size, size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                pixels[x, y] = Pixel(recipe, (x + 0.5) / size, (y + 0.5) / size);
            }
        }

        return CoverImage.FromPixels(pixels, "demo" + seed);
    }

    private static Recipe RecipeFor(int seed)
    {
        var r = new Random(seed);
        var hue = r.NextDouble() * 360;
        return new Recipe(hue, (hue + 40 + r.NextDouble() * 120) % 360, 0.3 + r.NextDouble() * 0.4, 0.3 + r.NextDouble() * 0.4, 0.15 + r.NextDouble() * 0.2, r.Next(3));
    }

    private static Color Pixel(Recipe p, double u, double v)
    {
        var d = Math.Sqrt((u - p.Cx) * (u - p.Cx) + (v - p.Cy) * (v - p.Cy));
        var t = p.Stripe == 0 ? v : p.Stripe == 1 ? u : (u + v) / 2;
        var inside = d < p.Rad;
        return FromHsl(inside ? p.Hue2 : p.Hue, 0.55, inside ? 0.62 : 0.18 + 0.32 * t);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Theme.Rgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }
}
