using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;

namespace Clims.Tui.Ui;

/// <summary>Where a cover should be drawn as a Sixel image, in screen cells. <paramref name="Style"/> is <see cref="CoverMode.Sixel"/> or a chunky style.</summary>
public sealed record SixelPlacement(CoverImage Image, int Cols, int Rows, Point Screen, CoverMode Style = CoverMode.Sixel)
{
    public string Key => $"{Image.Label}|{Cols}x{Rows}@{Screen.X},{Screen.Y}|{Style}";
}
