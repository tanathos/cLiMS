using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <param name="Bold">Optional part of <paramref name="Text"/> (start, length) drawn in bold.</param>
public sealed record Row(string Text, string Right = "", RowKind Kind = RowKind.Item, object? Data = null, (int Start, int Length)? Bold = null);
