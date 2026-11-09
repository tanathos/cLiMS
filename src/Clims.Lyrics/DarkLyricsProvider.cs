using System.Text;

namespace Clims.Lyrics;

/// <summary>
/// DarkLyrics (darklyrics.com): a metal lyrics archive with no API and no timestamps, read from the HTML of the album page,
/// which holds every song of the album: the others are returned as siblings, so one request fills the store for the whole album.
/// Requests are spaced out so the site is not hammered.
/// </summary>
public sealed class DarkLyricsProvider : ILyricsProvider
{
    private readonly HttpClient _http;
    private readonly TimeSpan _spacing;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    /// <param name="http">Client to use.</param>
    /// <param name="spacing">Minimum time between two requests to the site (default one second).</param>
    public DarkLyricsProvider(HttpClient http, TimeSpan? spacing = null)
    {
        _http = http;
        _spacing = spacing ?? TimeSpan.FromSeconds(1);
    }

    /// <inheritdoc />
    public string Name => "darklyrics";

    /// <inheritdoc />
    public async Task<LyricsResult?> FindAsync(LyricsQuery query, CancellationToken ct)
    {
        var artist = query.AlbumArtist is { Length: > 0 } ? query.AlbumArtist : query.Artist;
        if (LyricsKey.Fold(artist) is "various artists" or "various" or "va")
        {
            return null;
        }

        foreach (var url in DarkLyricsUrl.For(artist, query.Album))
        {
            var html = await GetAsync(url, ct).ConfigureAwait(false);
            if (html is null)
            {
                continue;
            }

            var songs = DarkLyricsParser.Parse(html);
            var title = LyricsKey.Fold(query.Title);
            var match = songs.Where(s => s.Text.Length > 0).Select(s => (s.Number, s.Title, s.Text, Folded: LyricsKey.Fold(s.Title))).ToList();
            var hit = match.FirstOrDefault(s => s.Folded == title);
            if (hit.Title is null && title.Length > 0)
            {
                hit = match.FirstOrDefault(s => s.Folded.Length > 0 && (s.Folded.Contains(title, StringComparison.Ordinal) || title.Contains(s.Folded, StringComparison.Ordinal)));
            }

            if (hit.Title is null)
            {
                continue;
            }

            var siblings = match
                .Where(s => s.Number != hit.Number)
                .Select(s => (new LyricsQuery(query.Artist, query.Album, s.Title, TimeSpan.Zero, query.AlbumArtist, s.Number), new LyricsResult(s.Text, null, Name)))
                .ToList();
            return new LyricsResult(hit.Text, null, Name) { Siblings = siblings };
        }

        return null;
    }

    private async Task<string?> GetAsync(string url, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var wait = _last + _spacing - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct).ConfigureAwait(false);
            }

            try
            {
                var bytes = await LyricsHttp.GetBytesAsync(_http, url, ct).ConfigureAwait(false);
                return bytes is null ? null : Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                _last = DateTimeOffset.UtcNow;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
