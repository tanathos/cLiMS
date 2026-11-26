namespace Clims.Tui.Ui;

/// <summary>
/// Square tiles of one to three units packed on a grid: each, in the order given, takes the first free place from the top,
/// left to right, so the smaller ones that come later fill the holes next to the larger ones.
/// </summary>
public sealed class MosaicLayout
{
    public const int UnitCols = 16;
    public const int UnitRows = 8;
    public const int GapCols = 2;
    public const int GapRows = 1;
    public const int MaxSpan = 3;

    private MosaicLayout(IReadOnlyList<MosaicTile> tiles)
    {
        Tiles = tiles;
        Width = tiles.Count == 0 ? 0 : tiles.Max(t => t.Right);
        Height = tiles.Count == 0 ? 0 : tiles.Max(t => t.Bottom);
    }

    /// <summary>One tile per item, in the order of the items.</summary>
    public IReadOnlyList<MosaicTile> Tiles { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The size an album gets for the number of times it was listened to.</summary>
    public static int SpanFor(int listens) => Math.Clamp(listens, 1, MaxSpan);

    /// <summary>Packs one tile per entry of <paramref name="spans"/> into an area.</summary>
    /// <param name="spans">The wanted size of each tile, in units.</param>
    /// <param name="width">Columns available.</param>
    /// <param name="viewRows">Rows on screen: no tile is made higher than that (but never smaller than one unit).</param>
    public static MosaicLayout Pack(IReadOnlyList<int> spans, int width, int viewRows)
    {
        var across = Math.Max(1, (width + GapCols) / (UnitCols + GapCols));
        var largest = Math.Clamp((viewRows + GapRows) / (UnitRows + GapRows), 1, Math.Min(MaxSpan, across));
        var used = new List<bool[]>();
        var tiles = new List<MosaicTile>(spans.Count);
        var firstOpen = 0; // rows above this one are full
        foreach (var wanted in spans)
        {
            var span = Math.Clamp(wanted, 1, largest);
            var (col, row) = Place(used, across, span, firstOpen);
            for (var r = row; r < row + span; r++)
            {
                while (used.Count <= r)
                {
                    used.Add(new bool[across]);
                }

                Array.Fill(used[r], true, col, span);
            }

            while (firstOpen < used.Count && Array.TrueForAll(used[firstOpen], u => u))
            {
                firstOpen++;
            }

            tiles.Add(new MosaicTile(col * (UnitCols + GapCols), row * (UnitRows + GapRows), span));
        }

        return new MosaicLayout(tiles);
    }

    private static (int Col, int Row) Place(List<bool[]> used, int across, int span, int from)
    {
        for (var row = from; ; row++)
        {
            for (var col = 0; col + span <= across; col++)
            {
                if (Free(used, col, row, span))
                {
                    return (col, row);
                }
            }
        }
    }

    private static bool Free(List<bool[]> used, int col, int row, int span)
    {
        for (var r = row; r < Math.Min(used.Count, row + span); r++)
        {
            for (var c = col; c < col + span; c++)
            {
                if (used[r][c])
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>The tile at a cell, or -1 (a gap or a hole).</summary>
    public int At(int x, int y)
    {
        for (var i = 0; i < Tiles.Count; i++)
        {
            if (Tiles[i].Contains(x, y))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The tile closest to a cell, or -1 when there is none.</summary>
    public int Nearest(int x, int y)
    {
        var best = -1;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < Tiles.Count; i++)
        {
            var t = Tiles[i];

            // Rows count double: a cell is twice as high as wide.
            var distance = Math.Max(0, Math.Max(t.X - x, x - (t.Right - 1))) + 2 * Math.Max(0, Math.Max(t.Y - y, y - (t.Bottom - 1)));
            if (distance < bestDistance)
            {
                (best, bestDistance) = (i, distance);
            }
        }

        return best;
    }

    /// <summary>
    /// The tile an arrow key goes to from tile <paramref name="from"/>: among those wholly on that side, first the ones facing
    /// it, then the closest, then the one nearest the top (or left). <paramref name="from"/> itself when there is none.
    /// </summary>
    /// <param name="from">The tile the arrow starts from.</param>
    /// <param name="dx">-1 left, 1 right, 0 up or down.</param>
    /// <param name="dy">-1 up, 1 down, 0 left or right.</param>
    public int Neighbor(int from, int dx, int dy)
    {
        var current = Tiles[from];
        var best = from;
        (int Aside, int Ahead, int Order) bestScore = (int.MaxValue, int.MaxValue, int.MaxValue);
        for (var i = 0; i < Tiles.Count; i++)
        {
            var t = Tiles[i];
            var ahead = dx > 0 ? t.X - current.Right
                : dx < 0 ? current.X - t.Right
                : dy > 0 ? t.Y - current.Bottom
                : current.Y - t.Bottom;
            if (i == from || ahead < 0)
            {
                continue;
            }

            var aside = dx != 0
                ? Math.Max(0, Math.Max(t.Y - current.Bottom + 1, current.Y - t.Bottom + 1))
                : Math.Max(0, Math.Max(t.X - current.Right + 1, current.X - t.Right + 1));
            var score = (aside, ahead, dx != 0 ? t.Y : t.X);
            if (score.CompareTo(bestScore) < 0)
            {
                (best, bestScore) = (i, score);
            }
        }

        return best;
    }
}
