namespace Clims.Info;

/// <summary>What to look up: an album and the artist it is listed under.</summary>
/// <param name="Artist">Album artist name.</param>
/// <param name="Album">Album title.</param>
/// <param name="ArtistId">The id the provider uses for this artist, when already known (it makes the album search exact).</param>
/// <param name="AlbumId">The id the provider uses for this album, when already known.</param>
/// <param name="WikidataId">The artist's Wikidata entity ("Q…") when an earlier provider found it: it names the artist's page on every wiki without guessing.</param>
/// <param name="AlbumWikidataId">The album's Wikidata entity ("Q…") when an earlier provider found it.</param>
public sealed record InfoQuery(string Artist, string Album, string? ArtistId = null, string? AlbumId = null, string? WikidataId = null, string? AlbumWikidataId = null);
