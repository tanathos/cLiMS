using System.Text.Json;

namespace Clims.Tui.Data;

/// <summary>A track of a saved queue. The file URL is the durable key: track ids can change after a rescan.</summary>
/// <param name="Url">File URL as LMS reports it.</param>
/// <param name="Title">Title, for listing a queue whose files are not loaded.</param>
/// <param name="Artist">Artist.</param>
/// <param name="Album">Album title.</param>
/// <param name="Seconds">Length in seconds.</param>
public sealed record SavedTrack(string Url, string Title, string? Artist, string? Album, double Seconds);
