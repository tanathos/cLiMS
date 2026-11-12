using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clims.Tui.Settings;

/// <summary>The JSON options of the settings file: indented, enums as names, property names read in any case.</summary>
public static class SettingsJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
