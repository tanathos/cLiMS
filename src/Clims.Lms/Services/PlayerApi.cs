using Clims.Lms.Json;

namespace Clims.Lms;

internal sealed class PlayerApi : IPlayerApi
{
    private readonly ILmsRpcClient _rpc;

    public PlayerApi(ILmsRpcClient rpc)
    {
        _rpc = rpc;
    }

    public async Task<PlayerStatus> GetStatusAsync(string playerId, int? start, int count, TrackTags tags, CancellationToken ct)
    {
        // "-" as the start index means "the current track" to LMS.
        object from = start is { } s ? s : "-";
        var r = await _rpc.RequestAsync(playerId, new object[] { "status", from, count, tags.ToTagArgument() }, RequestKind.Read, ct)
            .ConfigureAwait(false);
        return PlayerStatus.From(r, DateTimeOffset.UtcNow);
    }

    public Task PlayAsync(string playerId, CancellationToken ct) => Send(playerId, ct, "play");

    public Task PauseAsync(string playerId, bool? pause, CancellationToken ct) =>
        pause is { } p ? Send(playerId, ct, "pause", p ? 1 : 0) : Send(playerId, ct, "pause");

    public Task StopAsync(string playerId, CancellationToken ct) => Send(playerId, ct, "stop");

    public Task SeekAsync(string playerId, TimeSpan position, CancellationToken ct) =>
        Send(playerId, ct, "time", Invariant.Seconds(position < TimeSpan.Zero ? TimeSpan.Zero : position));

    public Task SeekByAsync(string playerId, TimeSpan delta, CancellationToken ct) =>
        Send(playerId, ct, "time", (delta < TimeSpan.Zero ? "-" : "+") + Invariant.Seconds(delta.Duration()));

    public Task SetVolumeAsync(string playerId, int volume, CancellationToken ct) =>
        Send(playerId, ct, "mixer", "volume", Math.Clamp(volume, 0, 100));

    public Task AdjustVolumeAsync(string playerId, int delta, CancellationToken ct) =>
        Send(playerId, ct, "mixer", "volume", (delta < 0 ? "-" : "+") + Math.Abs(delta));

    public Task SetMutedAsync(string playerId, bool muted, CancellationToken ct) =>
        Send(playerId, ct, "mixer", "muting", muted ? 1 : 0);

    public Task NextAsync(string playerId, CancellationToken ct) => Send(playerId, ct, "playlist", "index", "+1");

    public Task PreviousAsync(string playerId, CancellationToken ct) => Send(playerId, ct, "playlist", "index", "-1");

    public Task JumpToAsync(string playerId, int index, CancellationToken ct) =>
        Send(playerId, ct, "playlist", "index", Math.Max(0, index));

    public Task SetRepeatAsync(string playerId, RepeatMode mode, CancellationToken ct) =>
        Send(playerId, ct, "playlist", "repeat", (int)mode);

    public Task SetShuffleAsync(string playerId, ShuffleMode mode, CancellationToken ct) =>
        Send(playerId, ct, "playlist", "shuffle", (int)mode);

    public Task SetPowerAsync(string playerId, bool on, CancellationToken ct) => Send(playerId, ct, "power", on ? 1 : 0);

    private Task Send(string playerId, CancellationToken ct, params object[] command) =>
        _rpc.RequestAsync(playerId, command, RequestKind.Command, ct);
}
