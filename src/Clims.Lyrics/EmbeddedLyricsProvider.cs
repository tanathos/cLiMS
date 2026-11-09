namespace Clims.Lyrics;

/// <summary>
/// The lyrics written in the file's own tags (ID3 USLT, Vorbis LYRICS, MP4 lyrics), which LMS reads when it scans and hands
/// out through its <c>w</c> tag. Nothing is downloaded: it is one small question to the server about the track. Text that is
/// LRC (timestamps) comes out synced. Junk such as "XXX|" is not taken as lyrics.
/// </summary>
public sealed class EmbeddedLyricsProvider : ILyricsProvider
{
    private readonly Func<int, CancellationToken, Task<string?>> _fetch;

    /// <summary>Creates the provider.</summary>
    /// <param name="fetch">Asks the server for the lyrics tag of a track id (null when it has none). Throws <see cref="HttpRequestException"/> when the server cannot be reached.</param>
    public EmbeddedLyricsProvider(Func<int, CancellationToken, Task<string?>> fetch)
    {
        _fetch = fetch;
    }

    /// <inheritdoc />
    public string Name => "embedded";

    /// <inheritdoc />
    public bool IsOnline => false;

    /// <inheritdoc />
    public async Task<LyricsResult?> FindAsync(LyricsQuery query, CancellationToken ct)
    {
        if (query.TrackId is not { } id)
        {
            return null;
        }

        return LyricsText.From(await _fetch(id, ct).ConfigureAwait(false), Name);
    }
}
