using Clims.Lms.Json;

namespace Clims.Lms;

internal sealed class ServerApi : IServerApi
{
    private readonly ILmsRpcClient _rpc;

    public ServerApi(ILmsRpcClient rpc)
    {
        _rpc = rpc;
    }

    public async Task<string> GetVersionAsync(CancellationToken ct)
    {
        var r = await Read(ct, "version", "?").ConfigureAwait(false);
        return r.GetStringOrNull("_version") ?? string.Empty;
    }

    public async Task<ServerStatus> GetStatusAsync(CancellationToken ct) =>
        ServerStatus.From(await Read(ct, "serverstatus", 0, 50).ConfigureAwait(false));

    public async Task<IReadOnlyList<PlayerInfo>> GetPlayersAsync(CancellationToken ct)
    {
        var r = await Read(ct, "players", 0, 50).ConfigureAwait(false);
        return r.GetLoop("players_loop").Select(PlayerInfo.From).ToList();
    }

    public async Task<ScanState> GetScanStateAsync(CancellationToken ct) =>
        ScanState.From(await Read(ct, "rescanprogress").ConfigureAwait(false));

    public async Task<int> GetTrackCountAsync(CancellationToken ct) =>
        (await Read(ct, "info", "total", "songs", "?").ConfigureAwait(false)).GetInt("_songs");

    public async Task<bool> CanAsync(IReadOnlyList<string> command, CancellationToken ct)
    {
        var words = new List<object> { "can" };
        words.AddRange(command);
        words.Add("?");
        var r = await _rpc.RequestAsync(null, words, RequestKind.Read, ct).ConfigureAwait(false);
        return r.GetBool("_can");
    }

    private Task<System.Text.Json.JsonElement> Read(CancellationToken ct, params object[] command) =>
        _rpc.RequestAsync(null, command, RequestKind.Read, ct);
}
