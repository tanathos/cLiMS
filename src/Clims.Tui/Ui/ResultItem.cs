using Clims.Catalog;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>One row of the search results. Artists, albums and tracks take two lines and carry a small cover.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Text">Main line (the name or title; matches are highlighted).</param>
/// <param name="Detail">Second line, dim (artist, album, year; matches are highlighted).</param>
/// <param name="Right">Right-aligned text on the main line (duration, counts).</param>
/// <param name="Cover">Cover to show in the thumbnail; null shows the artist icon (artists) or nothing.</param>
/// <param name="Data">The catalogue object, or the group key of a "More" row.</param>
public sealed record ResultItem(ResultKind Kind, string Text, string Detail = "", string Right = "", CoverKey? Cover = null, object? Data = null)
{
    /// <summary>Rows of text this item occupies.</summary>
    public int Height => Kind is ResultKind.Artist or ResultKind.Album or ResultKind.Track ? 2 : 1;

    /// <summary>False for headers and status lines.</summary>
    public bool Selectable => Kind is not (ResultKind.Header or ResultKind.Info);
}
