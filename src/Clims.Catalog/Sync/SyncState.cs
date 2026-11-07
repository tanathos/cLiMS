using Clims.Lms;

namespace Clims.Catalog;

/// <summary>Where the local index stands compared with the server. The five states of <c>search.md</c> section 10, plus a demo state.</summary>
public enum SyncState
{
    /// <summary>No index on disk and no sync running.</summary>
    NotSynced,

    /// <summary>A sync is running; the Library tab shows what is loaded so far.</summary>
    Syncing,

    /// <summary>The index matches the server's last scan and counts.</summary>
    Synced,

    /// <summary>The server's last scan or counts moved; the old index keeps serving until the user agrees to resync.</summary>
    ServerChanged,

    /// <summary>The server cannot be reached; browsing uses the saved index, playback does not work.</summary>
    Offline,

    /// <summary>The server answered but rejected the login (or wants one). Nothing is retried until the user fixes it.</summary>
    LoginRequired,

    /// <summary>No server at all: built-in fake data for trying the UI.</summary>
    Demo,
}
