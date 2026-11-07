using Clims.Lms;

namespace Clims.Catalog;

/// <summary>Why the user is being asked whether to sync.</summary>
public enum SyncPromptReason
{
    /// <summary>There is no index yet.</summary>
    FirstBuild,

    /// <summary>The server's last scan or counts changed since the index was built.</summary>
    ServerChanged,
}
