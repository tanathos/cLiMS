namespace Clims.Catalog;

/// <summary>A genre.</summary>
/// <param name="Id">LMS genre id.</param>
/// <param name="Name">Genre name.</param>
public sealed record CatalogGenre(int Id, string Name);
