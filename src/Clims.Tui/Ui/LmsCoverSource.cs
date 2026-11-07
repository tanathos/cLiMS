using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Clims.Lms;
using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>
/// Fetches covers from the server only when a view shows them: 2 to 3 requests at a time, cancelled when scrolled
/// away, kept in a memory-only LRU (no disk cache, as decided). The server resizes, so no local scaling is needed.
/// </summary>
public sealed class LmsCoverSource : ICoverSource, IDisposable
{
    private const int RequestSize = 300;
    private const int BigRequestSize = 900;
    private const string BigSuffix = "|big";
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

        id = Id(key.ArtworkId, key.Big);

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
        var ids = keys.Where(k => k.ArtworkId is not null).Select(k => Id(k.ArtworkId!, k.Big)).ToHashSet();
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

    private static string Id(string artworkId, bool big) => big ? artworkId + BigSuffix : artworkId;

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

            var big = id.EndsWith(BigSuffix, StringComparison.Ordinal);
            var plain = big ? id[..^BigSuffix.Length] : id;
            var size = big ? BigRequestSize : RequestSize;
            using var request = new HttpRequestMessage(HttpMethod.Get, ArtworkUrl.Cover(_baseUri, plain, size, size));
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
