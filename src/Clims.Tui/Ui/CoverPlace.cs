namespace Clims.Tui.Ui;

/// <summary>A place that shows covers; each has its own <see cref="CoverMode"/> in the settings.</summary>
public enum CoverPlace
{
    /// <summary>The player footer.</summary>
    Footer,

    /// <summary>The library, recently added, search and genre lists.</summary>
    Library,

    /// <summary>The full-screen cover (the album info panel).</summary>
    FullScreen,
}
