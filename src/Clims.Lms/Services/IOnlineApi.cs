namespace Clims.Lms;

/// <summary>
/// The plugins that live under "Radio" and "Apps" on the server (Spotty, SpotOn, TIDAL, radio directories…). They all speak
/// the same browse protocol, so one implementation covers every plugin the server has installed.
/// </summary>
public interface IOnlineApi
{
    /// <summary>The radios and apps the server offers, in the server's order (radios first).</summary>
    Task<IReadOnlyList<OnlineApp>> GetAppsAsync(string playerId, CancellationToken cancellationToken = default);

    /// <summary>One level of an app's tree. A null <paramref name="itemId"/> is the top; <paramref name="search"/> fills in a search box.</summary>
    Task<OnlineListing> BrowseAsync(string playerId, string command, string? itemId, string? search, int start, int count, CancellationToken cancellationToken = default);

    /// <summary>Plays, appends or inserts what <paramref name="itemId"/> stands for.</summary>
    Task PlayAsync(string playerId, string command, string itemId, OnlineAction action, CancellationToken cancellationToken = default);

    /// <summary>How many tracks the player's queue holds now.</summary>
    Task<int> QueueLengthAsync(string playerId, CancellationToken cancellationToken = default);
}
