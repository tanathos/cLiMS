namespace Clims.Tui.Ui;

/// <summary>
/// The app's log file (<c>%LOCALAPPDATA%\cLiMS\clims.log</c>). A TUI owns the console, so nothing is ever printed there while
/// it runs: crashes, swallowed paint errors, failed commands and server problems go here. The file is cut down to its newest
/// half when it passes 1 MB.
/// </summary>
public static class CrashLog
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();

    /// <summary>Where the log goes. Tests point it at a temporary file.</summary>
    public static string Path { get; set; } = System.IO.Path.Combine(Clims.Catalog.CatalogPaths.DataFolder, "clims.log");

    /// <summary>Logs an exception. Returns the log path (for the message shown after a crash).</summary>
    public static string Write(Exception ex)
    {
        Append("ERROR", ex.ToString());
        return Path;
    }

    /// <summary>Logs a line of information (a failed command, a server problem, a state change worth knowing later).</summary>
    public static void Info(string message) => Append("INFO", message);

    private static void Append(string level, string text)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                if (File.Exists(Path) && new FileInfo(Path).Length > MaxBytes)
                {
                    var lines = File.ReadAllLines(Path);
                    File.WriteAllLines(Path, lines.Skip(lines.Length / 2));
                }

                File.AppendAllText(Path, $"[{DateTime.Now:O}] {level} {text}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never hide the original failure or stop the app.
            }
        }
    }
}
