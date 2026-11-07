using Clims.Lms;

namespace Clims.Catalog;

/// <summary>Outcome of comparing an index with the live server.</summary>
public enum AssessmentKind
{
    /// <summary>No index.</summary>
    NoIndex,

    /// <summary>Nothing changed.</summary>
    UpToDate,

    /// <summary>The server moved on.</summary>
    Changed,
}
