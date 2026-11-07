using Clims.Lms;

namespace Clims.Catalog;

/// <summary>A request to ask the user whether to sync now. The library never starts a sync on its own.</summary>
/// <param name="Reason">Why.</param>
/// <param name="Detail">Human readable detail (for example what changed).</param>
public sealed record SyncPrompt(SyncPromptReason Reason, string Detail);
