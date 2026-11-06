using System.Diagnostics;

namespace Clims.Tui.Ui;

/// <summary>Per-view paint timings for the feel test. Dumped on exit when CLIMS_PERF names a file.</summary>
public static class PerfStats
{
    private static readonly Dictionary<string, (int Count, double TotalMs, double MaxMs)> Stats = new();

    public static void Record(string view, TimeSpan elapsed)
    {
        var ms = elapsed.TotalMilliseconds;
        Stats.TryGetValue(view, out var s);
        Stats[view] = (s.Count + 1, s.TotalMs + ms, Math.Max(s.MaxMs, ms));
    }

    public static void Dump()
    {
        if (Environment.GetEnvironmentVariable("CLIMS_PERF") is not { Length: > 0 } path)
        {
            return;
        }

        var lines = Stats.Select(kv =>
            $"{kv.Key}: {kv.Value.Count} paints, avg {kv.Value.TotalMs / kv.Value.Count:F2} ms, max {kv.Value.MaxMs:F2} ms");
        File.WriteAllLines(path, lines.Append($"startup-to-exit process time {Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds:F1} s cpu"));
    }
}
