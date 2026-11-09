namespace Clims.Catalog;

/// <summary>A genre offered by the explorer, with how many of the matching tracks carry it.</summary>
/// <param name="GenreId">Genre id.</param>
/// <param name="Name">Genre name.</param>
/// <param name="Tracks">Matching tracks tagged with this genre.</param>
public sealed record GenreOption(int GenreId, string Name, int Tracks);
