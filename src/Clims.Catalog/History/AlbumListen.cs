namespace Clims.Catalog;

/// <summary>An album that was listened to (at least half of it), with how often.</summary>
/// <param name="AlbumId">LMS album id.</param>
/// <param name="Album">Album title.</param>
/// <param name="Artist">Album artist.</param>
/// <param name="LastListen">When the latest listen was completed.</param>
/// <param name="InWindow">Listens completed inside the window asked for.</param>
/// <param name="Total">Listens ever recorded.</param>
public sealed record AlbumListen(int AlbumId, string Album, string Artist, DateTimeOffset LastListen, int InWindow, int Total);
