using Clims.Lms;

namespace Clims.Catalog;

/// <summary>Search over the local index, with the server filling in the groups the index does not have yet.</summary>
public interface ISearchService
{
    /// <summary>Raised with the local result at once, and again when the server's part has arrived (any thread).</summary>
    event Action<SearchOutcome>? Changed;

    /// <summary>Starts a search. Supersedes the previous one: an answer for older text is never reported.</summary>
    void Query(string text);
}
