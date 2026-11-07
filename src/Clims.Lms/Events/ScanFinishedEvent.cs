namespace Clims.Lms;

/// <summary>A library scan finished. The server sends this twice, even when nothing changed; de-duplicated by the stream.</summary>
public sealed record ScanFinishedEvent : LmsEvent;
