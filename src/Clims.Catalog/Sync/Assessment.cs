using Clims.Lms;

namespace Clims.Catalog;

/// <summary>Result of <see cref="SyncAssessor.Assess"/>.</summary>
/// <param name="Kind">The outcome.</param>
/// <param name="Reason">What changed, for <see cref="AssessmentKind.Changed"/>.</param>
public sealed record Assessment(AssessmentKind Kind, string? Reason = null);
