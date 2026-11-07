using System.Security.Cryptography;
using System.Text;

namespace Clims.Tui;

/// <summary>
/// Keeps the server password out of plain text. On Windows the value is protected with DPAPI (readable only by the same
/// Windows user on this PC). Elsewhere nothing is stored: the password is asked again at each launch.
/// </summary>
public static class SecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("cLiMS server login");

    /// <summary>True when passwords can be remembered on this system.</summary>
    public static bool CanStore => OperatingSystem.IsWindows();

    /// <summary>Protects a password; null when this system cannot store one.</summary>
    public static string? Protect(string? password)
    {
        if (string.IsNullOrEmpty(password) || !OperatingSystem.IsWindows())
        {
            return null;
        }

        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Reads a protected password back; null when absent, damaged, or made by another user.</summary>
    public static string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue) || !OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
