using System.Net;
using Clims.Lms;

namespace Clims.Tui.Ui;

/// <summary>How the connect screen ended.</summary>
public enum ConnectChoice
{
    /// <summary>A server was reached; <see cref="ConnectResult.Uri"/> and the login are set.</summary>
    Connected,

    /// <summary>The user chose demo mode.</summary>
    Demo,

    /// <summary>The user backed out (keep what was running).</summary>
    Cancelled,
}
