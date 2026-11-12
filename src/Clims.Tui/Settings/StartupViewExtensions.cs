namespace Clims.Tui.Settings;

public static class StartupViewExtensions
{
    /// <summary>The position of the tab in the tab strip: Search, Library, Queue, Recently added, Genres.</summary>
    public static int TabIndex(this StartupView view) => view switch
    {
        StartupView.Search => 0,
        StartupView.Queue => 2,
        StartupView.Recent => 3,
        StartupView.Genres => 4,
        _ => 1,
    };
}
