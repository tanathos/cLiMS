namespace Clims.Tui.Ui;

/// <summary>How the footer draws the spectrum. F9 or a click on the spectrum goes to the next one.</summary>
public enum SpectrumStyle
{
    /// <summary>Block characters, one column per cell, 16 levels over two rows.</summary>
    Bars,

    /// <summary>The same with a gap between the bars.</summary>
    Slim,

    /// <summary>Bars growing up and down from the middle line, 8 levels each way.</summary>
    Mirror,

    /// <summary>Braille dots: two columns per cell, 8 levels over two rows.</summary>
    Braille,

    /// <summary>Bars whose upper row is drawn in the warning colour, so the loud parts stand out.</summary>
    Heat,

    /// <summary>The 12 notes (C, C#, D … B) with the octaves added up: the names above, the bars below, the strongest one highlighted. Needs the live feed.</summary>
    Chromagram,
}
