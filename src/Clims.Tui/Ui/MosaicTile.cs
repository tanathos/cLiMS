namespace Clims.Tui.Ui;

/// <summary>A square cover of a mosaic: where it is, in cells from the mosaic's top left corner.</summary>
/// <param name="X">Left column.</param>
/// <param name="Y">Top row.</param>
/// <param name="Span">Side of the tile in units: 1 is the smallest.</param>
public readonly record struct MosaicTile(int X, int Y, int Span)
{
    /// <summary>Width in cells: a cell is about twice as high as wide, so twice the rows make a square.</summary>
    public int Cols => Span * MosaicLayout.UnitCols + (Span - 1) * MosaicLayout.GapCols;

    /// <summary>Height in cells.</summary>
    public int Rows => Span * MosaicLayout.UnitRows + (Span - 1) * MosaicLayout.GapRows;

    /// <summary>The column after the tile.</summary>
    public int Right => X + Cols;

    /// <summary>The row after the tile.</summary>
    public int Bottom => Y + Rows;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
}
