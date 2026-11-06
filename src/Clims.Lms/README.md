# Clims.Lms

A small, UI-free .NET 10 client for [Lyrion Music Server](https://lyrion.org) (LMS): JSON-RPC over HTTP for queries and
commands, the telnet CLI socket for push events. No dependencies beyond the BCL. Trim and AOT friendly (no reflection:
replies are mapped by hand, see "Design").

## Quick start
```csharp
using var lms = new LmsClient(new LmsClientOptions { BaseUri = new Uri("http://192.168.1.20:9000/") });

var status = await lms.Server.GetStatusAsync();                       // version, totals, players
var albums = await lms.Library.GetAlbumsAsync(new AlbumQuery { Search = "iron" }, start: 0, count: 50);
var tracks = await lms.Library.GetTracksAsync(new TrackQuery { AlbumId = albums.Items[0].Id });
var cover  = ArtworkUrl.Cover(lms.BaseUri, albums.Items[0].ArtworkTrackId!, 96, 96);

var player = status.Players.First(p => p.IsHardwarePlayer).PlayerId;
await lms.Queue.AddTracksAsync(player, tracks.Items.Select(t => t.Id).ToList());   // gapless
await lms.Player.SeekByAsync(player, TimeSpan.FromSeconds(10));
```

Events:
```csharp
var events = new CliEventStream(new CliEventStreamOptions { Host = "192.168.1.20" });
await foreach (var e in events.ReadAllAsync(token))
{
    switch (e)
    {
        case TrackChangedEvent t: /* position is 0 now; re-query status for the rest */ break;
        case QueueChangedEvent: /* re-query status */ break;
        case EventStreamStateEvent { Connected: false }: /* show "offline"; it reconnects by itself */ break;
    }
}
```

## What is in the box
| Area | Types |
|---|---|
| Transport | `ILmsRpcClient` / `LmsRpcClient` (raw `RequestAsync(playerId, command[], kind)`), `LmsClientOptions` |
| Typed APIs | `ILibraryApi`, `IPlayerApi`, `IQueueApi`, `IServerApi`, facade `ILmsClient` / `LmsClient` |
| Models | `Album`, `Artist`, `Genre`, `Track`, `SavedPlaylist`, `PlayerInfo`, `PlayerStatus`, `QueueItem`, `ServerStatus`, `ScanState`, `SearchResults`, `Page<T>` |
| Requests | `AlbumQuery`, `TrackQuery`, `ArtistQuery`, `TrackTags` / `AlbumTags` flags (produce the `tags:` argument) |
| Paging | `Paging.EnumerateAsync` (`IAsyncEnumerable<T>` over start/count) |
| Events | `CliEventStream` (port 9090), `StatusPoller` (HTTP-only fallback), `LmsEventParser`, `LineDeduper`, event records |
| URLs | `ArtworkUrl.Cover(...)`, `AudioUrl.Download(...)` |
| Errors | `LmsConnectionException`, `LmsProtocolException`, `LmsAuthenticationException` (all derive from `LmsException`) |

## Things LMS does that this library hides (or that you must know)
- **No errors in-band.** A wrong command, unknown player id or oversized body makes LMS close the connection without
  replying. That becomes `LmsProtocolException` (with `Command` and `PlayerId`); an unreachable server or a timeout becomes
  `LmsConnectionException`. Protocol errors are not retried, connection errors are (reads only).
- **Loose typing.** `year`, `tracknum`, ids and booleans arrive as numbers or strings depending on the command. `JsonReading`
  accepts both. Empty results omit the `*_loop` key (`{"count":0}`, or just `{}` for playlists); that is an empty page.
- **Reads vs commands.** `RequestKind.Read` calls are retried after connection failures (default 2 retries, 250 ms doubling).
  `Command` calls never are: repeating "next track" after an ambiguous failure could skip two tracks.
- **Concurrency is capped** (default 3 in flight). LMS is mostly single-threaded and gets slow, sometimes to the 30 s timeout,
  while scanning. Check `Server.GetScanStateAsync()` and back off from heavy queries during a scan; the library does not do
  that for you.
- **Queue editing and gapless playback.** `Add*`, `Insert*Next`, `Remove`, `Move` edit the live queue and keep playback
  gapless. `Load*` replaces the queue and restarts the stream (an audible gap). Never use `Stop`/`Play` to advance. A natural
  track change is gapless, a manual `Next`/`JumpTo` re-buffers.
- **Id lists are chunked** at `MaxIdsPerRequest` (400; the request body limit is about 8 KB, 1 000 ids failed). `Load` replaces
  with the first chunk and appends the rest; `InsertTracksNext` sends chunks last-to-first so the order survives (every insert
  lands right after the current track).
- **Ids can change after a full rescan.** Persist `Track.Url` (the file URL) as the durable key and resolve it with
  `GetTrackByUrlAsync`.
- **Elapsed time** is only accurate at the moment of the reply and about a second off right after a track change. Keep
  `PlayerStatus.ReceivedAt` and call `ElapsedAt(now)` to interpolate; treat a `TrackChangedEvent` as position 0.
- **Events are hints.** Re-query `status` after queue/mode events (the CLI sends `ARRAY(0x...)` instead of the list).
  Duplicates are normal (`rescan done` fires twice; a seek repeats its events about 0.5 s later); `CliEventStream` drops
  identical lines within 250 ms. Skin and preference chatter is dropped. The stream reconnects with back-off (1 s up to
  30 s) and reports `EventStreamStateEvent`.
- **Tags are different for albums and tracks.** Artwork is `j` on `albums` and `J` on tracks; artist id is `S` vs `s`. Use
  the enums, not letters.
- **Auth.** Optional HTTP Basic credentials on the HTTP client. The CLI socket needs no login on a server without a
  password; `CliEventStream` does not log in. Cover and download URLs need the same credentials if protection is on.
  Untested against a protected server.
- **Never expose ports 9000/9090 beyond the LAN.**

## Design
- `HttpClient` is injected (or created and owned by `LmsClient`). No `ILogger` dependency yet; add one when the app needs it.
- Replies are mapped by hand from `JsonElement` (`Json/JsonReading.cs`) rather than with source-generated serializers: the
  shapes are too loose for plain deserialisation (numbers as strings, one object per field in `songinfo`, loops that
  vanish), and hand mapping needs no reflection or generated code.
- Services take the player id on every call, so the same client serves any number of players.
- `PlayerStatus` is a snapshot; nothing here polls behind your back.

## Tests
`tests/Clims.Lms.Tests` (xUnit, about 190 tests):
- unit tests over a fake `HttpMessageHandler` and **fixtures recorded from a real LMS 9.1.1** (`Fixtures/*.json`: serverstatus,
  players, artists, genres, albums, empty results, search, titles, status, songinfo, version, ...);
- real-socket tests: a listener that hangs up (protocol error), nothing listening (connection error), a fake CLI server for the
  event stream (subscribe, noise, duplicates, reconnect, cancel);
- read-only integration tests against a real server, enabled with `CLIMS_LMS_URL`:
  ```powershell
  $env:CLIMS_LMS_URL = "http://192.168.1.20:9000/"
  dotnet test tests/Clims.Lms.Tests
  ```
- mutating tests, off by default. They need `CLIMS_LMS_ALLOW_WRITE=1` **and** `CLIMS_LMS_PLAYER=<player id>`, take a
  `PlayerSnapshot` first and restore it afterwards. Do not run them while someone is listening.

Re-record fixtures only with read-only commands; they contain real library names.
