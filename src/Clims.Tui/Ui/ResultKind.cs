using Clims.Catalog;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

public enum ResultKind
{
    /// <summary>A group title such as "Albums (4 / 12)"; not selectable.</summary>
    Header,
    Artist,
    Album,
    Track,
    Genre,

    /// <summary>"More tracks…": expands a group.</summary>
    More,

    /// <summary>A status line ("Searching the server…", "No results"); not selectable.</summary>
    Info,
}
