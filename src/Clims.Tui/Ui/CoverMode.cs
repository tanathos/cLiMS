using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

public enum CoverMode
{
    /// <summary>"▀": 1 x 2 samples per cell, two colours.</summary>
    HalfBlock,

    /// <summary>Quadrant blocks: 2 x 2 samples per cell, two colours.</summary>
    Quadrant,

    /// <summary>Braille dots: 2 x 4 samples per cell, two colours.</summary>
    Braille,

    /// <summary>Real pixels via Sixel when the terminal supports it (partly visible covers fall back to quadrants).</summary>
    Sixel,
}
