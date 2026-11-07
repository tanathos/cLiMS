using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>One line of a <see cref="ContextMenu"/>.</summary>
public sealed record MenuEntry(string Label, Action Run);
