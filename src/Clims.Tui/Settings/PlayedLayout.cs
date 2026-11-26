namespace Clims.Tui.Settings;

/// <summary>How the Played tab shows the albums of the last week and month.</summary>
public enum PlayedLayout
{
    /// <summary>Covers of one size with title, artist and listen count, the latest listened first.</summary>
    Grid,

    /// <summary>Covers only, packed as a mosaic: the more often an album was listened to, the larger its cover.</summary>
    Mosaic,
}
