using System.Globalization;
using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

/// <summary>A player known to the server (a Squeezelite instance, a web client, ...).</summary>
/// <param name="PlayerId">Player id, normally the MAC address; pass it to player commands.</param>
/// <param name="Name">Display name.</param>
/// <param name="Model">Model string such as <c>squeezelite</c> or <c>http</c>.</param>
/// <param name="Ip">IP address and port of the player.</param>
/// <param name="IsConnected">True while the player is connected to the server.</param>
/// <param name="IsPlaying">True while playing.</param>
/// <param name="IsPoweredOn">Power state.</param>
/// <param name="IsHardwarePlayer">False for web clients such as a browser tab.</param>
public sealed record PlayerInfo(
    string PlayerId,
    string Name,
    string? Model,
    string? Ip,
    bool IsConnected,
    bool IsPlaying,
    bool IsPoweredOn,
    bool IsHardwarePlayer)
{
    internal static PlayerInfo From(JsonElement e) =>
        new(
            e.GetStringOrNull("playerid") ?? string.Empty,
            e.GetStringOrNull("name") ?? string.Empty,
            e.GetStringOrNull("model"),
            e.GetStringOrNull("ip"),
            e.GetBool("connected"),
            e.GetBool("isplaying"),
            e.GetBool("power"),
            !e.TryGet("isplayer", out _) || e.GetBool("isplayer"));
}
