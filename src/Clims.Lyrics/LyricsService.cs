namespace Clims.Lyrics;

/// <summary>
/// Finds the lyrics of a track: the store first (a hit costs nothing and works offline), then the providers in order.
/// What is found is stored for good; a search that found nothing is remembered for <c>missRetry</c>, unless a provider
/// could not be reached (then the answer may be different later, so nothing is remembered).
/// </summary>
public sealed class LyricsService
{
    private readonly LyricsStore _store;
    private readonly IReadOnlyList<ILyricsProvider> _providers;
    private readonly TimeSpan _missRetry;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="store">Where lyrics are kept.</param>
    /// <param name="providers">Providers in the order they are asked (empty: the store only).</param>
    /// <param name="missRetry">How long a "not found" is believed.</param>
    /// <param name="clock">Time source (tests).</param>
    public LyricsService(LyricsStore store, IReadOnlyList<ILyricsProvider> providers, TimeSpan missRetry, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _providers = providers;
        _missRetry = missRetry;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>True when there is at least one provider to ask.</summary>
    public bool CanSearch => _providers.Count > 0;

    /// <summary>True when at least one provider asks the internet (the others only look at the tags and files of the track).</summary>
    public bool CanSearchOnline => _providers.Any(p => p.IsOnline);

    /// <summary>The lyrics already stored for a track, without any search.</summary>
    public LyricsResult? Stored(LyricsQuery query) => _store.Get(query.Key);

    /// <summary>
    /// The lyrics of a track, or null. <paramref name="force"/> searches again even though something is stored (a better
    /// version may exist) and keeps the stored one when the search finds nothing; hand-made lyrics are never searched again.
    /// </summary>
    public async Task<LyricsResult?> FindAsync(LyricsQuery query, bool force = false, CancellationToken ct = default)
    {
        var key = query.Key;
        var stored = _store.Get(key);
        if (stored is not null && (!force || _store.IsUserEdited(key)))
        {
            return stored;
        }

        var names = _providers.Select(p => p.Name).ToList();
        if (!force && stored is null && names.Count > 0 && _store.IsRecentMiss(key, _clock(), _missRetry, names))
        {
            return null;
        }

        var unreachable = false;
        foreach (var provider in _providers)
        {
            ct.ThrowIfCancellationRequested();
            LyricsResult? found;
            try
            {
                found = await provider.FindAsync(query, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or TimeoutException && !ct.IsCancellationRequested)
            {
                unreachable = true;
                continue;
            }

            if (found is null)
            {
                continue;
            }

            var now = _clock();
            _store.Put(query, found, now);
            foreach (var (sibling, result) in found.Siblings)
            {
                if (sibling.Key != key)
                {
                    _store.Put(sibling, result, now, replace: false);
                }
            }

            return found;
        }

        if (!unreachable && names.Count > 0 && stored is null)
        {
            _store.PutMiss(key, _clock(), names);
        }

        return stored;
    }
}
