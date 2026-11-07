using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;

namespace Clims.Tui.Ui;

/// <summary>Where a cover should be drawn as a Sixel image, in screen cells.</summary>
public sealed record SixelPlacement(CoverImage Image, int Cols, int Rows, Point Screen)
{
    public string Key => $"{Image.Label}|{Cols}x{Rows}@{Screen.X},{Screen.Y}";
}
