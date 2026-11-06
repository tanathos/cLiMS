using Clims.Lms.Json;

namespace Clims.Lms;

internal sealed class QueueApi : IQueueApi
{
    private readonly ILmsRpcClient _rpc;
    private readonly int _maxIds;

    public QueueApi(ILmsRpcClient rpc, LmsClientOptions options)
    {
        _rpc = rpc;
        _maxIds = Math.Max(1, options.MaxIdsPerRequest);
    }

    public async Task<Page<QueueItem>> GetQueueAsync(string playerId, int start, int count, TrackTags tags, CancellationToken ct)
    {
        var r = await _rpc.RequestAsync(playerId, new object[] { "status", start, count, tags.ToTagArgument() }, RequestKind.Read, ct)
            .ConfigureAwait(false);
        var status = PlayerStatus.From(r, DateTimeOffset.UtcNow);
        return new Page<QueueItem>(status.Items, status.QueueLength, start);
    }

    public async Task LoadTracksAsync(string playerId, IReadOnlyList<int> trackIds, int playIndex, CancellationToken ct)
    {
        // The first chunk replaces the queue; the rest are appended in order.
        var first = true;
        foreach (var chunk in Chunk(trackIds))
        {
            if (first)
            {
                await Control(playerId, ct, "cmd:load", Ids(chunk), $"play_index:{Math.Max(0, playIndex)}").ConfigureAwait(false);
                first = false;
            }
            else
            {
                await Control(playerId, ct, "cmd:add", Ids(chunk)).ConfigureAwait(false);
            }
        }
    }

    public async Task AddTracksAsync(string playerId, IReadOnlyList<int> trackIds, CancellationToken ct)
    {
        foreach (var chunk in Chunk(trackIds))
        {
            await Control(playerId, ct, "cmd:add", Ids(chunk)).ConfigureAwait(false);
        }
    }

    public async Task InsertTracksNextAsync(string playerId, IReadOnlyList<int> trackIds, CancellationToken ct)
    {
        // Every insert lands directly after the current track, so later chunks would end up in front of earlier
        // ones. Sending the chunks last-to-first keeps the requested order.
        foreach (var chunk in Chunk(trackIds).Reverse())
        {
            await Control(playerId, ct, "cmd:insert", Ids(chunk)).ConfigureAwait(false);
        }
    }

    public Task LoadAlbumAsync(string playerId, int albumId, int playIndex, CancellationToken ct) =>
        Control(playerId, ct, "cmd:load", $"album_id:{albumId}", $"play_index:{Math.Max(0, playIndex)}");

    public Task AddAlbumAsync(string playerId, int albumId, CancellationToken ct) =>
        Control(playerId, ct, "cmd:add", $"album_id:{albumId}");

    public Task InsertAlbumNextAsync(string playerId, int albumId, CancellationToken ct) =>
        Control(playerId, ct, "cmd:insert", $"album_id:{albumId}");

    public async Task RemoveAsync(string playerId, IReadOnlyCollection<int> indices, CancellationToken ct)
    {
        // Highest index first, so earlier deletions do not shift the ones still to come.
        foreach (var index in indices.Distinct().OrderByDescending(i => i))
        {
            await _rpc.RequestAsync(playerId, new object[] { "playlist", "delete", index }, RequestKind.Command, ct).ConfigureAwait(false);
        }
    }

    public Task MoveAsync(string playerId, int fromIndex, int toIndex, CancellationToken ct) =>
        _rpc.RequestAsync(playerId, new object[] { "playlist", "move", fromIndex, toIndex }, RequestKind.Command, ct);

    public Task ClearAsync(string playerId, CancellationToken ct) =>
        _rpc.RequestAsync(playerId, new object[] { "playlist", "clear" }, RequestKind.Command, ct);

    /// <summary>Splits an id list into request-sized chunks, preserving order.</summary>
    internal IEnumerable<IReadOnlyList<int>> Chunk(IReadOnlyList<int> ids) => ids.Chunk(_maxIds).Select(c => (IReadOnlyList<int>)c);

    private static string Ids(IReadOnlyList<int> ids) => "track_id:" + string.Join(',', ids);

    private Task Control(string playerId, CancellationToken ct, params object[] arguments) =>
        _rpc.RequestAsync(playerId, new object[] { "playlistcontrol" }.Concat(arguments).ToArray(), RequestKind.Command, ct);
}
