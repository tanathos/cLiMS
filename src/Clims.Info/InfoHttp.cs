using System.Net;

namespace Clims.Info;

/// <summary>What every request has in common: an honest User-Agent, and a mapping of status codes to "not found" or "could not ask".</summary>
internal static class InfoHttp
{
    /// <summary>Sent with every request, so the sites can tell what is calling them (MusicBrainz and Wikimedia require it).</summary>
    public const string UserAgent = "cLiMS (+https://github.com/tanathos/cLiMS)";

    /// <summary>
    /// The body for a 200, null for a 404. Other statuses throw <see cref="HttpRequestException"/>: the site is unwell or busy,
    /// which says nothing about what was asked.
    /// </summary>
    public static async Task<byte[]?> GetAsync(HttpClient http, string url, string? accept, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        if (accept is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept", accept);
        }

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }
}
