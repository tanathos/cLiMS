using System.Globalization;
using System.Text.Json;

namespace Clims.Lms.Json;

/// <summary>
/// Tolerant readers for LMS replies. LMS types its JSON loosely: the same field can be a number or a string
/// (<c>year</c> is a number on <c>albums</c> and a string on <c>titles</c>), booleans are 0/1, and a missing or
/// null value means "not present". These helpers hide that so models never see raw JSON.
/// </summary>
public static class JsonReading
{
    /// <summary>Finds a property and treats JSON null as missing.</summary>
    public static bool TryGet(this JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out value)
            && value.ValueKind != JsonValueKind.Null
            && value.ValueKind != JsonValueKind.Undefined)
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Reads a string; numbers and booleans are returned as their text. Null when absent.</summary>
    public static string? GetStringOrNull(this JsonElement element, string name) =>
        element.TryGet(name, out var v) ? AsString(v) : null;

    /// <summary>Reads an int from a number or numeric string; <paramref name="fallback"/> when absent or not numeric.</summary>
    public static int GetInt(this JsonElement element, string name, int fallback = 0) =>
        element.GetIntOrNull(name) ?? fallback;

    /// <summary>Reads an int from a number or numeric string; null when absent or not numeric.</summary>
    public static int? GetIntOrNull(this JsonElement element, string name)
    {
        if (!element.TryGet(name, out var v))
        {
            return null;
        }

        var d = AsDouble(v);
        return d is null ? null : (int)Math.Clamp(d.Value, int.MinValue, int.MaxValue);
    }

    /// <summary>Reads a double from a number or numeric string; null when absent or not numeric.</summary>
    public static double? GetDoubleOrNull(this JsonElement element, string name) =>
        element.TryGet(name, out var v) ? AsDouble(v) : null;

    /// <summary>Reads a double; <paramref name="fallback"/> when absent or not numeric.</summary>
    public static double GetDouble(this JsonElement element, string name, double fallback = 0) =>
        element.GetDoubleOrNull(name) ?? fallback;

    /// <summary>Reads a boolean from true/false, 0/1 or "0"/"1".</summary>
    public static bool GetBool(this JsonElement element, string name) =>
        element.TryGet(name, out var v) && v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => AsDouble(v) is { } d && d != 0,
        };

    /// <summary>Reads a duration given in seconds (possibly fractional).</summary>
    public static TimeSpan GetSeconds(this JsonElement element, string name) =>
        TimeSpan.FromSeconds(Math.Max(0, element.GetDouble(name)));

    /// <summary>
    /// Items of a <c>*_loop</c> array. LMS omits the key when there are no results (<c>{"count":0}</c>, or
    /// just <c>{}</c>), which is returned as an empty sequence.
    /// </summary>
    public static IEnumerable<JsonElement> GetLoop(this JsonElement element, string name) =>
        element.TryGet(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private static string? AsString(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

    private static double? AsDouble(JsonElement v)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.Number:
                return v.TryGetDouble(out var n) ? n : null;
            case JsonValueKind.String:
                return double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : null;
            default:
                return null;
        }
    }
}
