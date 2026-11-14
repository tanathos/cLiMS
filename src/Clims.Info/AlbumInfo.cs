namespace Clims.Info;

/// <summary>What a provider knows about an album.</summary>
/// <param name="Provider">Name of the provider that answered.</param>
/// <param name="Id">The provider's id for the album.</param>
/// <param name="Title">Title.</param>
/// <param name="Kind">"Album", "EP", "Single"…, or null.</param>
/// <param name="Secondary">Further kinds ("Live", "Compilation"…).</param>
/// <param name="FirstReleased">Date of the first release (a year or a full date), or null.</param>
/// <param name="Genres">Genres, the most usual first.</param>
/// <param name="Rating">Average rating from 0 to 5, or null.</param>
/// <param name="Votes">How many votes the rating comes from.</param>
/// <param name="Label">Record label of the first release, or null.</param>
/// <param name="CatalogNumber">Catalogue number at that label, or null.</param>
/// <param name="Country">Country of that release, or null.</param>
/// <param name="TrackCount">Number of tracks of that release, or null.</param>
/// <param name="Links">Web addresses.</param>
/// <param name="WikidataId">The album's Wikidata entity ("Q…"), or null.</param>
/// <param name="Description">A text about the album (what it is, how it was made and received), or null.</param>
/// <param name="DescriptionSource">Name of the provider the description came from (its licence is the provider's), or null.</param>
public sealed record AlbumInfo(
    string Provider,
    string Id,
    string Title,
    string? Kind,
    IReadOnlyList<string> Secondary,
    string? FirstReleased,
    IReadOnlyList<string> Genres,
    double? Rating,
    int Votes,
    string? Label,
    string? CatalogNumber,
    string? Country,
    int? TrackCount,
    IReadOnlyList<InfoLink> Links,
    string? WikidataId = null,
    string? Description = null,
    string? DescriptionSource = null);
