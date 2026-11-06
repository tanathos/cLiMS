using System.Net.Http.Json;
using System.Text.Json;
using Clims.Tui.Ui;

namespace Clims.Tui.Data;

/// <summary>
/// TEMPORARY Step 1 spike: pulls a few random album covers from a real LMS (read-only requests) so the fake
/// library can be judged with real artwork. Replaced by the Clims.Lms client and cover service in Steps 2 and 3;
/// delete this file then (UI code must not talk HTTP).
/// </summary>
public static class DevCovers
{
    public static async Task<List<CoverImage>> LoadAsync(string baseUrl, int count, CancellationToken ct = default)
    {
        baseUrl = baseUrl.TrimEnd('/');
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        // Ask for a few extra: albums without artwork come back as a placeholder we may want to skip.
        var albums = await Rpc(http, baseUrl, ["albums", 0, count + 6, "tags:al", "sort:random"], ct);
        var covers = new List<CoverImage>();
        foreach (var album in albums.GetProperty("albums_loop").EnumerateArray())
        {
            if (covers.Count >= count)
            {
                break;
            }

            var id = album.GetProperty("id").GetInt32();
            var title = album.GetProperty("album").GetString() ?? "?";
            var artist = album.TryGetProperty("artist", out var a) ? a.GetString() ?? "?" : "?";

            // The cover URL is addressed by a track id of the album.
            var tracks = await Rpc(http, baseUrl, ["titles", 0, 1, $"album_id:{id}", "tags:a"], ct);
            if (!tracks.TryGetProperty("titles_loop", out var loop) || loop.GetArrayLength() == 0)
            {
                continue;
            }

            var trackId = loop[0].GetProperty("id").GetInt32();
            var bytes = await http.GetByteArrayAsync($"{baseUrl}/music/{trackId}/cover_300x300_o.jpg", ct);
            if (CoverImage.Decode(bytes, $"{artist} - {title}") is { } cover)
            {
                covers.Add(cover);
            }
        }

        return covers;
    }

    private static async Task<JsonElement> Rpc(HttpClient http, string baseUrl, object[] command, CancellationToken ct)
    {
        var body = new { id = 1, method = "slim.request", @params = new object[] { "", command } };
        using var response = await http.PostAsJsonAsync($"{baseUrl}/jsonrpc.js", body, ct);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.GetProperty("result").Clone();
    }
}
