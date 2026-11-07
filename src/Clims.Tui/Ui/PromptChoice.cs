using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>One answer of a prompt: the key that picks it, the label, and what it does.</summary>
/// <param name="Key">Letter that selects it (case-insensitive).</param>
/// <param name="Label">Text on the button.</param>
/// <param name="Run">Action when chosen.</param>
public sealed record PromptChoice(char Key, string Label, Action Run);
