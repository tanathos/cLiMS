namespace Clims.Catalog;

/// <summary>What the search service reports after each keystroke, and again when the server has answered.</summary>
/// <param name="Query">The text that was searched.</param>
/// <param name="Result">The (possibly mixed) result.</param>
/// <param name="ServerGroups">Groups that are not in the local index and so came, or will come, from the server.</param>
/// <param name="ServerPending">True while the server's answer for <see cref="ServerGroups"/> is still on its way.</param>
/// <param name="Note">Something the user should know (the server could not be reached); null when all is well.</param>
public sealed record SearchOutcome(string Query, RankedSearchResult Result, SearchGroups ServerGroups, bool ServerPending, string? Note);
