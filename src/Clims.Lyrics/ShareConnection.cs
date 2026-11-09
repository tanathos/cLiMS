using System.Runtime.InteropServices;

namespace Clims.Lyrics;

/// <summary>
/// Signs this PC in to a Windows network share (<c>\\nas\Music</c>) with a user name and password, for the current process only.
/// Elsewhere (Linux, macOS) shares are mounted by the system and this does nothing.
/// </summary>
public static class ShareConnection
{
    private const int NoError = 0;
    private const int AlreadyConnectedWithOtherCredentials = 1219;
    private const int ResourceTypeDisk = 1;

    /// <summary>The <c>\\host\share</c> part of a UNC path, or null when the path is not a network path.</summary>
    public static string? RootOf(string path)
    {
        if (!path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return null;
        }

        var parts = path[2..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? @"\\" + parts[0] + @"\" + parts[1] : null;
    }

    /// <summary>Connects to the share; throws <see cref="IOException"/> when Windows refuses (wrong login, share not there).</summary>
    /// <param name="root">A <c>\\host\share</c> path.</param>
    /// <param name="user">The user name (<c>user</c>, <c>NAS\user</c> or <c>DOMAIN\user</c>).</param>
    /// <param name="password">The password.</param>
    public static void Connect(string root, string user, string password)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var resource = new NetResource { Type = ResourceTypeDisk, RemoteName = root };
        var code = WNetAddConnection2(ref resource, password, user, 0);
        if (code is NoError)
        {
            return;
        }

        if (code is AlreadyConnectedWithOtherCredentials)
        {
            // This PC already has a login on that server (for example from Explorer); Windows allows one per server. Use it.
            return;
        }

        throw new IOException($"Could not sign in to {root} as {user} (Windows error {code}).");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NetResource resource, string? password, string? userName, int flags);
}
