namespace Clims.Lyrics;

/// <summary>One line of synced lyrics.</summary>
/// <param name="Time">When the line starts, from the start of the track (the file's own offset already applied).</param>
/// <param name="Text">The words; empty for an instrumental gap.</param>
public sealed record LyricLine(TimeSpan Time, string Text);
