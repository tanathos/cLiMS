namespace Clims.Tui.Ui;

/// <summary>Appends unhandled UI exceptions to a log in the temp folder so crashes can be diagnosed after the terminal is restored.</summary>
public static class CrashLog
{
    public static string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clims-crash.log");

    public static string Write(Exception ex)
    {
        try
        {
            File.AppendAllText(Path, $"[{DateTime.Now:O}]\n{ex}\n\n");
        }
        catch (IOException)
        {
            // Logging must never hide the original failure.
        }

        return Path;
    }
}
