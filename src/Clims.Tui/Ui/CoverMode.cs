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

    /// <summary>Sextant blocks (Unicode 13): 2 x 3 samples per cell, two colours.</summary>
    Sextant,

    /// <summary>Octant blocks (Unicode 16): 2 x 4 samples per cell as solid blocks, two colours.</summary>
    Octant,

    /// <summary>Octant blocks (2 x 4 samples per cell) in the four greens of a Game Boy, with ordered (Bayer) dithering.</summary>
    GameBoy,

    /// <summary>Octant blocks (2 x 4 samples per cell) in the 16 colours of a Commodore 64, with ordered (Bayer) dithering.</summary>
    C64,

    /// <summary>Sixel, but from a small picture (about 48 x 48 pixels) enlarged to square blocks. Cells where Sixel is not possible.</summary>
    Chunky,

    /// <summary>Like <see cref="Chunky"/> (about 64 x 64 pixels) with a darker line under each block row, like a CRT.</summary>
    ChunkyScanlines,

    /// <summary>Like <see cref="GameBoy"/> with quadrant blocks (2 x 2 samples per cell), which every font has.</summary>
    GameBoyQuadrant,

    /// <summary>Like <see cref="C64"/> with quadrant blocks (2 x 2 samples per cell), which every font has.</summary>
    C64Quadrant,
}
