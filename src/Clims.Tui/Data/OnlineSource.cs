using Clims.Lms;

namespace Clims.Tui.Data;

/// <summary>What the Online tab needs: the server's plugin browser and the id of the player that plays what is chosen (null until it is known).</summary>
public sealed record OnlineSource(IOnlineApi Api, Func<string?> PlayerId);
