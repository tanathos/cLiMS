namespace Clims.Info;

/// <summary>A member of a band.</summary>
/// <param name="Name">Name of the person.</param>
/// <param name="Role">What they play or do ("guitar", "vocals"…), or null.</param>
/// <param name="Begin">When they joined (a year or a date), or null.</param>
/// <param name="End">When they left, or null.</param>
/// <param name="Current">True when they have not left.</param>
public sealed record InfoMember(string Name, string? Role, string? Begin, string? End, bool Current);
