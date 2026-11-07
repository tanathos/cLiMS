using Clims.Catalog;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>What the user asked to do with a track or album besides playing it right now.</summary>
public enum QueueAction
{
    /// <summary>Append to the end of the queue.</summary>
    Add,

    /// <summary>Insert right after the current track.</summary>
    PlayNext,
}
