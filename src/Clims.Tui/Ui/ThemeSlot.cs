namespace Clims.Tui.Ui;

/// <summary>
/// A colour the interface is painted with. A theme gives a value to every slot, and settings.json can override any of them by name
/// (<c>"UI": { "Theme": { "Colors": { "Accent": "#FF8800" } } }</c>). To add a colour: add a slot here, a property in
/// <see cref="Theme"/> and a value in every theme of <see cref="Themes"/> (a test fails when one is missing).
/// </summary>
public enum ThemeSlot
{
    /// <summary>Background of the pages.</summary>
    Bg,

    /// <summary>Background of the header bar.</summary>
    BgHeader,

    /// <summary>Alternate rows, buttons, inputs.</summary>
    BgAlt,

    /// <summary>The selected row.</summary>
    BgSelected,

    /// <summary>The selected row of a list that does not have the focus.</summary>
    BgSelectedDim,

    /// <summary>Normal text.</summary>
    Fg,

    /// <summary>Secondary text.</summary>
    FgDim,

    /// <summary>Headings, links, the played part of things.</summary>
    Accent,

    /// <summary>Success.</summary>
    Good,

    /// <summary>Errors.</summary>
    Bad,

    /// <summary>Warnings and values that can be changed.</summary>
    Warn,

    /// <summary>Background under the pointer on clickable things.</summary>
    HoverBg,

    /// <summary>Text of a link under the pointer.</summary>
    HoverFg,

    /// <summary>Frames, scrollbar tracks.</summary>
    Border,

    /// <summary>Barely visible things: placeholders, unplayed markers.</summary>
    Faint,

    /// <summary>Section markers of the album view.</summary>
    Marker,

    /// <summary>The thumb of scrollbars.</summary>
    Thumb,
}
