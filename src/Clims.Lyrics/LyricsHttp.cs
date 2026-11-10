using System.Net;

namespace Clims.Lyrics;

/// <summary>What every provider request has in common: an honest User-Agent, and a mapping of status codes to "not found" or "could not ask".</summary>
internal static class LyricsHttp
{
    /// <summary>Sent with every request, so the sites can tell what is calling them.</summary>
    public const string UserAgent = "cLiMS (+https://github.com/tanathos/cLiMS)";

    /// <summary>
    /// The response body for a 200, null for a 404 (the provider does not have it). Other statuses throw
    /// <see cref="HttpRequestException"/>: the provider is unwell, which says nothing about the song.
    /// </summary>
    public static async Task<byte[]?> GetBytesAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }
}
