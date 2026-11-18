namespace Clims.Catalog;

/// <summary>How often a genre was played, and which other genres its tracks carried along.</summary>
/// <param name="Genre">Genre name.</param>
/// <param name="Plays">Plays of tracks tagged with the genre.</param>
/// <param name="Together">The other genres on those tracks, with the plays that carried both; most played first. Empty for a co-genre itself.</param>
public sealed record GenrePlays(string Genre, int Plays, IReadOnlyList<GenrePlays> Together);
