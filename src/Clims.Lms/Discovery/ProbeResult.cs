using System.Net;

namespace Clims.Lms;

/// <summary>Result of <see cref="ServerProbe.CheckAsync"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Message">A sentence for the user.</param>
/// <param name="Version">Server version when connected.</param>
/// <param name="Uuid">Server UUID when connected.</param>
public sealed record ProbeResult(ProbeOutcome Outcome, string Message, string? Version = null, string? Uuid = null);
