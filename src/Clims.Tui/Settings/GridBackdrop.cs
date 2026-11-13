namespace Clims.Tui.Settings;

/// <summary>What fills the background of the tracklist panel that opens under a cover of the grid layout.</summary>
public enum GridBackdrop
{
    /// <summary>The cover itself, enlarged and blocky, mixed into the theme background so the text stays readable.</summary>
    Pixellated,

    /// <summary>One flat colour taken from the cover, mixed into the theme background.</summary>
    Tint,
}
