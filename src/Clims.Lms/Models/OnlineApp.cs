namespace Clims.Lms;

/// <summary>An entry of the server's "Radio" or "Apps" list: a plugin (Spotty, SpotOn, TIDAL…) or a radio directory that can be browsed.</summary>
/// <param name="Name">What the server calls it.</param>
/// <param name="Command">The command word that browses it (<c>spotty</c>, <c>tidal</c>, <c>presets</c>…).</param>
/// <param name="IsRadio">True when it comes from the server's radio list, false for the apps list.</param>
/// <param name="IsSearch">True when the entry is a search box (it needs text before it lists anything).</param>
public sealed record OnlineApp(string Name, string Command, bool IsRadio, bool IsSearch);
