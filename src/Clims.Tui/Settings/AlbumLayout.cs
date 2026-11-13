namespace Clims.Tui.Settings;

/// <summary>How the Library and Recently added tabs list albums.</summary>
public enum AlbumLayout
{
    /// <summary>One block per album: cover on the left, details and tracklist on the right.</summary>
    List,

    /// <summary>A grid of covers with title and artist; a click on a cover opens its tracklist in a panel under that row.</summary>
    Grid,
}
