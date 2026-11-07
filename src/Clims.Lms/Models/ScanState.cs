using System.Globalization;
using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms;

/// <summary>Library scan progress from <c>rescanprogress</c>.</summary>
/// <param name="IsScanning">True while a scan runs. A scan takes minutes and disconnects players at the end.</param>
/// <param name="Details">Raw progress fields (step names and percentages) while scanning.</param>
public sealed record ScanState(bool IsScanning, IReadOnlyDictionary<string, string> Details)
{
    internal static ScanState From(JsonElement r)
    {
        var details = new Dictionary<string, string>();
        if (r.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in r.EnumerateObject())
            {
                if (p.Name != "rescan" && r.GetStringOrNull(p.Name) is { } text)
                {
                    details[p.Name] = text;
                }
            }
        }

        return new ScanState(r.GetBool("rescan"), details);
    }
}
