namespace Clims.Catalog;

/// <summary>An album artist as listed by the server. Albums refer to it by <see cref="Id"/>.</summary>
/// <param name="Id">LMS artist id (can change after a full rescan; a resync refreshes it).</param>
/// <param name="Name">Display name.</param>
public sealed record CatalogArtist(int Id, string Name)
{
    /// <summary>
    /// The A-Z jump key: first letter ignoring a leading "The ", digits grouped as <c>#</c> and everything else as <c>#</c> too.
    /// </summary>
    public string TextKey => TextKeys.For(Name);
}
