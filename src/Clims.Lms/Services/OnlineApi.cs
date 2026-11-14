using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

/// <summary>Default <see cref="IOnlineApi"/>.</summary>
public sealed class OnlineApi : IOnlineApi
{
    private readonly ILmsRpcClient _rpc;

    /// <summary>Creates the API over the server's JSON-RPC client.</summary>
    public OnlineApi(ILmsRpcClient rpc)
    {
        _rpc = rpc;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OnlineApp>> GetAppsAsync(string playerId, CancellationToken ct)
    {
        var apps = new List<OnlineApp>();
        foreach (var (word, radio) in new[] { ("radios", true), ("apps", false) })
        {
            var reply = await _rpc.RequestAsync(playerId, new object[] { word, 0, 200 }, RequestKind.Read, ct).ConfigureAwait(false);
            foreach (var e in Loop(reply))
            {
                var command = e.GetStringOrNull("cmd");
                var type = e.GetStringOrNull("type") ?? string.Empty;
                if (string.IsNullOrEmpty(command) || !type.StartsWith("xmlbrowser", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = (e.GetStringOrNull("name") ?? command).Trim();
                if (!radio && string.Equals(name, "Sounds & Effects", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // the server's sleep-sound plugin: nothing to play on a music player
                }

                apps.Add(new OnlineApp(name, command, radio,type.EndsWith("_search", StringComparison.OrdinalIgnoreCase)));
            }
        }

        return apps;
    }

    /// <inheritdoc />
    public async Task<OnlineListing> BrowseAsync(string playerId, string command, string? itemId, string? search, int start, int count, CancellationToken ct)
    {
        var words = new List<object> { command, "items", start, count };
        if (!string.IsNullOrEmpty(itemId))
        {
            words.Add("item_id:" + itemId);
        }

        if (!string.IsNullOrEmpty(search))
        {
            words.Add("search:" + search);
        }

        var reply = await _rpc.RequestAsync(playerId, words, RequestKind.Read, ct).ConfigureAwait(false);
        var items = Loop(reply).Select(OnlineItem.From).ToList();
        return new OnlineListing(reply.GetStringOrNull("title") ?? string.Empty, items, Math.Max(reply.GetInt("count"), start + items.Count), start);
    }

    /// <inheritdoc />
    public Task PlayAsync(string playerId, string command, string itemId, OnlineAction action, CancellationToken ct) =>
        _rpc.RequestAsync(
            playerId,
            [command, "playlist", action switch { OnlineAction.Add => "add", OnlineAction.Insert => "insert", _ => "play" }, "item_id:" + itemId],
            RequestKind.Command,
            ct);

    /// <inheritdoc />
    public async Task<int> QueueLengthAsync(string playerId, CancellationToken ct)
    {
        var reply = await _rpc.RequestAsync(playerId, new object[] { "status", "-", 1 }, RequestKind.Read, ct).ConfigureAwait(false);
        return reply.GetInt("playlist_tracks");
    }

    /// <summary>The lines of a reply: the plugins name the array after themselves (<c>loop_loop</c>, <c>appss_loop</c>…).</summary>
    private static IEnumerable<JsonElement> Loop(JsonElement reply)
    {
        if (reply.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in reply.EnumerateObject())
        {
            if (property.Name.EndsWith("_loop", StringComparison.Ordinal) && property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in property.Value.EnumerateArray())
                {
                    yield return e;
                }

                yield break;
            }
        }
    }
}
