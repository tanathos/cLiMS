using Clims.Catalog;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Clims.Tui.Ui;

/// <summary>The left pane's selection: which dimension, which key within it (null = "All ..."), and the heading to show.</summary>
public sealed record LibrarySelection(LibraryDimension Dimension, object? Key, string Heading);
