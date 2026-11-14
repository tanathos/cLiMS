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
    private readonly Action<string>? _log;

    /// <summary>Creates the service.</summary>
    /// <param name="store">Where lyrics are kept.</param>
    /// <param name="providers">Providers in the order they are asked (empty: the store only).</param>
    /// <param name="missRetry">How long a "not found" is believed.</param>
    /// <param name="clock">Time source (tests).</param>
    /// <param name="log">Receives a line for each search: what was asked, what every provider answered, which result was taken (null: no log).</param>
    public LyricsService(LyricsStore store, IReadOnlyList<ILyricsProvider> providers, TimeSpan missRetry, Func<DateTimeOffset>? clock = null, Action<string>? log = null)
    {
        _store = store;
        _providers = providers;
        _missRetry = missRetry;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _log = log;
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
    /// <paramref name="otherThan"/> names the provider the stored lyrics came from: with <paramref name="force"/> it is skipped, and the
    /// providers after it are asked first (then the ones before it), so repeating the request walks through all of them.
    /// </summary>
    public async Task<LyricsResult?> FindAsync(LyricsQuery query, bool force = false, CancellationToken ct = default, string? otherThan = null)
    {
        var key = query.Key;
        var who = $"Lyrics \"{query.Artist} – {query.Title}\" ({query.Album})";
        var stored = _store.Get(key);
        if (stored is not null && (!force || _store.IsUserEdited(key)))
        {
            _log?.Invoke($"{who}: stored copy used (source {stored.Source})");
            return stored;
        }

        var names = _providers.Select(p => p.Name).ToList();
        if (!force && stored is null && names.Count > 0 && _store.IsRecentMiss(key, _clock(), _missRetry, names))
        {
            _log?.Invoke($"{who}: not searched, the same providers found nothing recently");
            return null;
        }

        var order = Order(force ? otherThan : null).Select(p => p.Name).ToList();
        _log?.Invoke($"{who}: searching {(order.Count == 0 ? "(no providers)" : string.Join(", ", order))}{(force ? (otherThan is null ? " (forced)" : $" (forced, other than {otherThan})") : "")}");
        var unreachable = false;
        foreach (var provider in Order(force ? otherThan : null))
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
                _log?.Invoke($"{who}: {provider.Name} could not be reached ({ex.Message})");
                continue;
            }

            if (found is null)
            {
                _log?.Invoke($"{who}: {provider.Name} has no match");
                continue;
            }

            _log?.Invoke($"{who}: {provider.Name} matched, selected ({(found.Instrumental ? "instrumental" : found.IsSynced ? $"synced, {found.Lines.Count} lines" : "plain text")}{(found.Siblings.Count > 0 ? $", {found.Siblings.Count} other tracks of the album stored too" : "")})");

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

        _log?.Invoke($"{who}: no provider had it{(unreachable ? " (some could not be reached, so nothing is remembered)" : "")}{(stored is not null ? "; the stored copy is kept" : "")}");
        if (!unreachable && names.Count > 0 && stored is null)
        {
            _store.PutMiss(key, _clock(), names);
        }

        return stored;
    }

    /// <summary>The providers in the order to ask them: as configured, or, after <paramref name="skip"/> was used, the ones that follow it first and it left out.</summary>
    private IEnumerable<ILyricsProvider> Order(string? skip)
    {
        var at = skip is null ? -1 : _providers.ToList().FindIndex(p => p.Name == skip);
        if (at < 0)
        {
            return _providers;
        }

        return _providers.Skip(at + 1).Concat(_providers.Take(at));
    }
}
