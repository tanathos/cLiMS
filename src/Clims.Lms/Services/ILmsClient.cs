namespace Clims.Lms;

/// <summary>Entry point: the typed APIs plus the raw client for commands that are not wrapped.</summary>
public interface ILmsClient
{
    /// <summary>Base address of the server, used to build art and download URLs.</summary>
    Uri BaseUri { get; }

    /// <summary>Raw JSON-RPC access.</summary>
    ILmsRpcClient Rpc { get; }

    /// <summary>Library queries.</summary>
    ILibraryApi Library { get; }

    /// <summary>Transport and status.</summary>
    IPlayerApi Player { get; }

    /// <summary>Queue editing.</summary>
    IQueueApi Queue { get; }

    /// <summary>Server information.</summary>
    IServerApi Server { get; }
}
