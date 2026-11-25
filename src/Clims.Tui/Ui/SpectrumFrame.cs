namespace Clims.Tui.Ui;

/// <summary>The two text rows of a spectrum: a character per cell and how to colour it.</summary>
public sealed class SpectrumFrame
{
    /// <summary>Drawn in the normal bar colour.</summary>
    public const byte Normal = 0;

    /// <summary>Drawn with the colours swapped: a block that hangs from the top of its cell (the lower half of a mirrored bar).</summary>
    public const byte Inverted = 1;

    /// <summary>Drawn in the warning colour.</summary>
    public const byte Hot = 2;

    public SpectrumFrame(int width)
    {
        Top = new char[width];
        Bottom = new char[width];
        TopKind = new byte[width];
        BottomKind = new byte[width];
        Array.Fill(Top, ' ');
        Array.Fill(Bottom, ' ');
    }

    public char[] Top { get; }

    public char[] Bottom { get; }

    public byte[] TopKind { get; }

    public byte[] BottomKind { get; }
}
