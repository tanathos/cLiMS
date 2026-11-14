namespace Clims.Info;

/// <summary>What a provider knows about an artist.</summary>
/// <param name="Provider">Name of the provider that answered.</param>
/// <param name="Id">The provider's id for the artist.</param>
/// <param name="Name">Name.</param>
/// <param name="Kind">"Group", "Person"…, or null.</param>
/// <param name="Country">Country code or name, or null.</param>
/// <param name="Area">Where the artist is from (a city or region), or null.</param>
/// <param name="Begin">When it started: a year or a date, or null.</param>
/// <param name="End">When it ended, or null.</param>
/// <param name="Ended">True when the artist is no longer active.</param>
/// <param name="Disambiguation">The provider's short note telling two artists of the same name apart, or null.</param>
/// <param name="Biography">A biography, or null when the provider has none.</param>
/// <param name="Genres">Genres, the most usual first.</param>
/// <param name="Members">The members of a band.</param>
/// <param name="Links">Web addresses.</param>
/// <param name="ImageUrls">Addresses of pictures of the artist, the best first.</param>
/// <param name="WikidataId">The artist's Wikidata entity ("Q…"), or null.</param>
/// <param name="BiographySource">Name of the provider the biography came from (its licence is the provider's), or null.</param>
public sealed record ArtistInfo(
    string Provider,
    string Id,
    string Name,
    string? Kind,
    string? Country,
    string? Area,
    string? Begin,
    string? End,
    bool Ended,
    string? Disambiguation,
    string? Biography,
    IReadOnlyList<string> Genres,
    IReadOnlyList<InfoMember> Members,
    IReadOnlyList<InfoLink> Links,
    IReadOnlyList<string> ImageUrls,
    string? WikidataId = null,
    string? BiographySource = null);
