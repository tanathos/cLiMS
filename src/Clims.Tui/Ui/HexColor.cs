using System.Globalization;
using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>Colours written as <c>#RRGGBB</c> (the # is optional, <c>#RGB</c> is accepted too).</summary>
public static class HexColor
{
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        var hex = text?.Trim().TrimStart('#') ?? "";
        if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        }

        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        color = Theme.Rgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        return true;
    }

    public static string Format(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
