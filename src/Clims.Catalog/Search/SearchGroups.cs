namespace Clims.Catalog;

/// <summary>Which groups a search had to ask the server for.</summary>
[Flags]
public enum SearchGroups
{
    /// <summary>All groups came from the local index.</summary>
    None = 0,

    /// <summary>Artists.</summary>
    Artists = 1,

    /// <summary>Albums.</summary>
    Albums = 2,

    /// <summary>Tracks.</summary>
    Tracks = 4,

    /// <summary>Genres.</summary>
    Genres = 8,

    /// <summary>Everything.</summary>
    All = Artists | Albums | Tracks | Genres,
}
