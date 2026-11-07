namespace Clims.Lms;

/// <summary>Server-wide information.</summary>
public interface IServerApi
{
    /// <summary>Server version text, for example 9.1.1.</summary>
    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>Version, library totals, last scan time and players in one call.</summary>
    Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists players.</summary>
    Task<IReadOnlyList<PlayerInfo>> GetPlayersAsync(CancellationToken cancellationToken = default);

    /// <summary>Current scan progress. Back off from heavy queries while a scan runs.</summary>
    Task<ScanState> GetScanStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Total tracks in the library: a cheap poll to detect library changes.</summary>
    Task<int> GetTrackCountAsync(CancellationToken cancellationToken = default);

    /// <summary>Asks the server whether a command exists (<c>can playlistcontrol ?</c>).</summary>
    Task<bool> CanAsync(IReadOnlyList<string> command, CancellationToken cancellationToken = default);
}
