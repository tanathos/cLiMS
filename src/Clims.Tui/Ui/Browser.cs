using System.Diagnostics;

namespace Clims.Tui.Ui;

/// <summary>Opens a web address in the user's default browser.</summary>
public static class Browser
{
    /// <summary>True for an absolute http or https address (nothing else is ever handed to the system to open).</summary>
    public static bool IsWebAddress(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Opens the address; false when it is not a web address or the system could not open it.</summary>
    /// <param name="url">The address.</param>
    public static bool Open(string? url)
    {
        if (!IsWebAddress(url))
        {
            return false;
        }

        try
        {
            ProcessStartInfo start;
            if (OperatingSystem.IsWindows())
            {
                start = new ProcessStartInfo(url!) { UseShellExecute = true };
            }
            else
            {
                // The opener must not write over the screen of the terminal app.
                start = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                start.ArgumentList.Add(url!);
            }

            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
