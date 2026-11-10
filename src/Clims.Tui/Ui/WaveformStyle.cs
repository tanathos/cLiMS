namespace Clims.Tui.Ui;

/// <summary>How the footer draws the waveform of the playing track. Click the waveform to go to the next one.</summary>
public enum WaveformStyle
{
    /// <summary>Block characters, one column per cell, 16 levels over two rows.</summary>
    Blocks,

    /// <summary>Braille dots: two columns per cell, 8 levels over two rows.</summary>
    Braille,

    /// <summary>A real image (Sixel): thin bars from the bottom. Braille where the terminal has no Sixel.</summary>
    Sixel,
}
