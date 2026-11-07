using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

public sealed record Row(string Text, string Right = "", RowKind Kind = RowKind.Item, object? Data = null);
