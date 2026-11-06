# cLiMS – JSON-RPC client library research

Research only, nothing implemented. Proposal: a **separate class library** (working name `Clims.Lms`, `net10.0`, no UI dependencies) referenced by the TUI. No maintained .NET client for LMS/Lyrion was found on NuGet/GitHub (only a 2010 CodeProject article), so we write a thin one.

✔ = tested on your server (LMS 9.1.1). Other facts are from the [Lyrion CLI reference](https://lyrion.org/reference/cli/introduction/): [using-the-cli](https://lyrion.org/reference/cli/using-the-cli/), [general](https://lyrion.org/reference/cli/general/), [database](https://lyrion.org/reference/cli/database), [playlist](https://lyrion.org/reference/cli/playlist), [players](https://lyrion.org/reference/cli/players/), [notifications](https://lyrion.org/reference/cli/notifications), [compound queries](https://lyrion.org/reference/cli/compoundqueries/).

## 1. Protocol facts
- Request: `POST /jsonrpc.js`, body `{"id":1,"method":"slim.request","params":[playerId|"", [cmd, start, count, "tag:value", ...]]}`. Global commands use `""`. ✔
- `id` is optional and echoed back ✔. The response echoes `method`, `params`, `id`; data is under `result`.
- Positional replies use `_name` keys (`{"_version":"9.1.1"}`, `pref` gives `{"_p2":"0"}`). Tagged replies use tag names as keys.
- **No in-band errors.** The docs say invalid requests give `{}` or ECONNRESET. ✔ An unknown command, an unknown player MAC, an unknown `method` and non-JSON all returned an empty reply / closed connection (curl exit 52; .NET "response ended prematurely").
- **JSON-RPC batch (array body) is not supported** ✔ (connection closed). Use compound queries or parallel requests.
- Paging: `start` (0-based) + `count`; reply has `count` (total) and a `<name>_loop` array (`albums_loop`, `artists_loop`, `titles_loop`, `playlist_loop`, `players_loop`). **With zero results the loop key is absent** (`{"count":0}`) ✔; `playlists` with none saved returns `{}` ✔.
- **Loose typing** ✔: `playlist_cur_index:"5"` and `tracknum:"4"` are strings, `year` is a number on `albums` but a string on `titles`, `time`/`duration`/`playlist_timestamp` are doubles, `artwork_track_id`/`coverid` are hex strings. The client needs tolerant string-or-number converters.
- `tags:` letters choose the fields (`id` and `title` always present). Useful ones: a artist, l album, d duration, t tracknum, y year, u url, K artwork_url, J artwork_track_id, c coverid, e album_id, s artist_id, p genre_id, g genre, i disc, q disccount, r bitrate, T samplerate, I samplesize, Q lossless, C compilation, R rating, O playcount, w lyrics. `albums` has its own set: `l y j t i q w W a S s X`.
- Filters: `artist_id`, `album_id`, `genre_id`, `track_id`, `role_id`, `year`, `compilation:0|1`, `search:`, `library_id`, `sort:`. `album_id`/`track_id` override other filters on most queries. `["search",0,10,"term:x"]` returns `artists_count/albums_count/tracks_count/genres_count` plus loops.
- `can <cmd…> ?` checks whether a command exists ✔ (`can playlist addtracks ?` gives 1, `can playlistcontrol ?` gives 1).
- Auth: none documented for JSON-RPC; the telnet CLI uses `login user pass` when Security is enabled. Your server needs none ✔. With a password the HTTP server is probably behind Basic auth (untested), so the library should accept optional credentials.

## 2. Command inventory to wrap
**Library:** `artists`, `albums`, `titles`, `genres`, `years`, `roles`, `search`, `songinfo`, `musicfolder`, `playlists`, `playlists tracks`, `info total …`, `libraries`, `rescan`, `rescanprogress`.

**Transport:** `play`, `pause [0|1]`, `stop`, `mode ?`, `time <n|+n|-n|?>` (seek), `power`, `mixer volume <n|+n|-n|?>`, `mixer muting`, `playlist index <n|+n|-n>` (next/previous/jump), `playlist repeat 0|1|2`, `playlist shuffle 0|1|2`.

**Queue:**
- `status <start> <count> tags:…` (queue plus current state; `playlist_timestamp` for change detection)
- `playlist play|add|insert <item>`, `playlist deleteitem <item>`, `playlist delete <index>`, `playlist move <from> <to>`, `playlist clear`
- `playlist loadtracks|addtracks|inserttracks|deletetracks <searchparam>`
- `playlistcontrol cmd:load|add|insert|delete` with `track_id:1,2,3`, `album_id`, `artist_id`, `genre_id`, `playlist_id` or `folder_id` (optional `play_index`; returns `count`)
- `playlist save <name>`, `playlist resume <name>`, `playlists new|rename|delete|edit` (server-side saved playlists)

**Server:** `serverstatus`, `players`, `sync`, `syncgroups`, `pref`, `playerpref`, `version ?`, `can`.

Notes: no `playlist insertlist` or `playlist load` is documented. `playlistcontrol cmd:insert` means "play next". **`playlistcontrol … track_id:a,b,c` adds an explicit ordered list of IDs in one call**, which is the key primitive for custom queues.

## 3. Proposed library shape
```
Clims.Lms (net10.0, nullable, trim/AOT-friendly: System.Text.Json source generators)
 ├─ Transport/  LmsRpcClient          raw request: (playerId, command[]) -> JsonElement/typed
 │              LmsClientOptions      BaseUri, credentials, timeout, retry
 │              (HttpClient injected: IHttpClientFactory/DI friendly, ILogger)
 ├─ Commands/   request builders + TagSet (flags enum -> "tags:acdl…")
 ├─ Models/     Album, Artist, Genre, Track, SavedPlaylist, PlayerInfo, PlayerStatus, QueueItem, Page<T>
 ├─ Services/   ILibraryApi, IPlayerApi (transport), IQueueApi, IServerApi (facade: ILmsClient)
 ├─ Paging/     IAsyncEnumerable<T> over start/count; Page<T>(Items, Total)
 ├─ Events/     ILmsEventStream (telnet CLI :9090 `listen`/`subscribe`) -> IAsyncEnumerable<LmsEvent>
 │              + StatusPoller fallback (status every N s, diff on playlist_timestamp)
 ├─ Art/        ArtworkUrl (/music/<id>/cover_WxH_o.jpg) and AudioUrl (/music/<id>/download) helpers
 └─ Json/       tolerant converters (string-or-number, empty-object-as-empty)
Clims.Lms.Tests  fixtures recorded from the real server + fake HttpMessageHandler
```
Design points:
- **Typed layer over a raw layer.** Callers can always drop to `RequestAsync("…", "mixer", "volume", 30)` for unwrapped commands.
- **Error mapping.** Empty `{}` or a reset connection becomes `LmsProtocolException` (command and player in the message); network failures become `LmsConnectionException`. Since the server gives no detail, cache the `players` list to validate IDs and use `can` at startup.
- **Concurrency.** Limit parallel calls (LMS is single-threaded Perl; 2–4 at a time is enough). Use cancellation tokens and per-call timeouts. Retry only idempotent reads.
- **Events** need a persistent TCP socket (line protocol, percent-encoded, UTF-8, `listen 1`), so they live in a separate class from the HTTP client. Notifications of interest: `playlist newsong|stop|pause`, `mixer volume`, `play`, `pause`, `power`, and the queue-changing playlist commands (all commands also echo as notifications; queries and `status` are not pushed). Treat an event as "something changed", then re-query `status` and diff `playlist_timestamp`.
- **Time.** Expose `PlayerStatus.Elapsed` plus `ReceivedAt`, so the UI and waveform can interpolate between polls.
- **IDs.** LMS ids may change after a rescan. Use ids within a session and the file `url` as the durable key for persisted custom queues.

## 4. Pagination and performance (your library: 2 008 albums, 1 004 artists, 19 948 tracks) ✔
- Never load all tracks. Page with `start/count` (100–500 per call) and fetch per album/artist lazily. Use `count` for virtual scrolling.
- Request the minimum `tags:` per view (album list, track list, queue); replies are large.
- Cache by `(command, filters)` and invalidate on `rescan done` / `library changed`. Keep a small artwork cache.
- `albums`/`artists` replies include `textkey` (first letter), useful for A–Z navigation.

## 5. Custom queues and gapless, in API terms
The local queue store keeps track `url`s (durable) plus last-known ids.

| Intent | Calls | Gapless-safe? |
|---|---|---|
| Play queue Q now (explicit user action) | `playlistcontrol cmd:load track_id:…` | **No**: replaces the playlist, new stream |
| Append Q after current | `playlistcontrol cmd:add track_id:…` | Yes |
| Play next | `playlistcontrol cmd:insert track_id:…` | Yes (effect on the pre-buffered next track to confirm) |
| Replace "upcoming" with Q, keep current | read `status`, delete indices after current (from the end), then `cmd:add` | Yes in principle, needs a test |
| Reorder | `playlist move from to` | Yes unless it touches current/next |
| Skip/jump | `playlist index +1` / `n` | Natural advance is gapless; a manual jump re-buffers |

Open point: resolving a track `url` to an `id` (`songinfo url:…` or `titles` + `search`) when ids changed after a rescan.

## 6. Quirks observed on your server (encode in tests)
1. Invalid command, unknown player or array body: connection closed with no HTTP response.
2. `albums`/`titles` with no match: `{"count":0}`, no `*_loop`.
3. `playlists` with none saved: `{}`.
4. Mixed number/string typing (see section 1).
5. `status` while paused returned `mode:"pause"`, `time:390.41`, `duration:432.52`, `playlist_cur_index:"5"`, `playlist_tracks:10`, `playlist_timestamp`, and a `playlist_loop` containing only the requested window (start `"-"` gives the current track).
6. `serverstatus` gives `version`, `uuid`, `player count`, `players_loop` with `playerid`, `name`, `model`, `ip`, `connected`, `isplaying`, `power`.
7. `pref transitionType ?` (server pref) returned null; the **player** pref returned `0`. Use `playerpref`.

## 7. Open questions: findings

Read-only tests on your server (✔) plus web search. I changed nothing on the server. Forum and advisory claims come from search summaries, not full reads.

1. **Auth on `/jsonrpc.js`: yes, HTTP Basic.** A Home Assistant user got `401 Unauthorized` from `http://…:9000/jsonrpc.js` after enabling password protection ([thread](https://community.home-assistant.io/t/squeezebox-authorisation-problem/43141)). Third-party LMS tools expose "HTTP basic auth" username/password options ([example](https://glama.ai/mcp/servers/eggs-gd/lms-mcp/tree)). A `#` in the password reportedly caused trouble. Library: optional `Authorization: Basic` header (UTF-8). Not tested here because that means enabling a password on your server.
   - Related: an advisory ([ZSL-2026-5991](https://www.zeroscience.mk/advisories/ZSL-2026-5991.html), CVE-2026-50233) says LMS 9.2.0 lets an unauthenticated `readdirectory` query list folders outside the media dirs. Your server is 9.1.1. Whether it is affected or fixed was not confirmed. Do not expose 9000/9090 beyond the LAN.
   - Bug 8967 ([link](https://bugs-archive.lyrion.org/bug-8967.html)) is about password protection for `download.mp3`/`stream.mp3`. Expect the audio-download URL to need the same credentials.
2. **Telnet CLI (9090): no login needed** ✔. Findings:
   - `version ?`, `info total songs ?` (19948), `<mac> mode ?`, `<mac> playlist tracks ?` and `rescanprogress` all work unauthenticated. `listen 1` and `can listen ?` also work.
   - Replies are **percent-encoded** (`b8%3A27%3A…`, `?` becomes `%3F`), and the client must decode them.
   - An unknown command is **echoed back** with `?` encoded, with no error. So errors are undetectable here too.
   - Not tested: whether `playlist newsong` arrives promptly at a gapless boundary. That needs a track to play, which I did not trigger.
3. **Gapless and queue edits near the end of a track: a real risk.** Forum reports ([1](https://forums.lyrion.org/forum/user-forums/logitech-media-server/1658961-tracks-added-to-current-playlist-not-playing-if-player-buffer-has-already-been-filled), [2](https://forums.lyrion.org/forum/user-forums/logitech-media-server/94826-add-to-playlist-next-and-other-similar-functionality)):
   - If the player has already been handed the last queued track (buffer full), tracks added or inserted afterwards may not play. Playback stops at the end of the original list and needs a manual skip.
   - A developer comment says changing this is hard because the next track may already be in the player's buffer.
   - Client rules: always keep at least 2 tracks ahead; add to the queue early, not in the last seconds of the final track; after an append, check `mode` at the end of a track and, if stopped with tracks remaining, issue `playlist index +1`. Inserting in the middle of the queue is not reported as affected.
   - Needs a listening test on your Pi with the pCP buffer settings (needs a queue and playback on your player).
4. **`playlistcontrol` with many `track_id`s: supported, limit unknown.** Bug 1354 ([link](https://bugs-archive.lyrion.org/bug-1354.html)) added comma-separated lists (`cmd:add track_id:44,34,123,88,1`), marked fixed. No size limit documented. Library: chunk at about 200 ids per call, appending in order; a real limit test needs the queue changed.
5. **Detecting library changes:** `info total songs ?` ✔ (19948), `rescanprogress` ✔ (`rescan:0` when idle), plus the `rescan done` and `library changed` notifications on the CLI socket. Use the notification, with the song count as a cheap poll fallback.
6. **CometD/`subscribe:` for push over HTTP:** a long-poll CometD interface exists (60 s poll timeout, 3 min auto-disconnect; echo-id quirk in [issue 765](https://github.com/LMS-Community/slimserver/issues/765)), but I found no usable request format. Decision: use the telnet CLI socket for events, and drop CometD.

**Remaining (need your OK to change player/queue state):** event timing at gapless boundaries, appending near the end of the queue, the `playlistcontrol` size limit, effect of `insert`/`delete` on pre-buffering, and Basic-auth behaviour with a password.

## 8. Live tests on the player (Squeezelite `v2.0.0-1476-pCP`, FLAC/MP3, no crossfade)

Run with the player muted. State was snapshotted first and restored afterwards (queue, index 5, position 390.41 s, paused, volume 70, unmuted, no saved playlists left). Timings are wall-clock from the test script, ±0.1 s. These checks watch the control plane only: events, `mode`, `time` and index. I could not hear or record the audio, so "no gap" below means no stall in `time`/`mode`, not a proven sample-exact gapless transition (that still needs the `-v` capture from section 4c).

| # | Test | Result |
|---|---|---|
| 1 | Natural advance FLAC to FLAC (track 7 to 8) | `playlist newsong` event arrived 0.05 s after the old track's end. `mode` stayed `play`, `time` went 292.72, then 0.50 on the next poll. No stall. |
| 1 | When does LMS "open" the next track? | About 1 s after a track starts (`playlist open <next url>` events), i.e. the next track is pre-buffered long before the end. |
| 2 | `playlistcontrol cmd:insert` (play next) after the next track was already pre-opened | Worked. LMS re-opened the inserted track (`playlist inserttracks … index:10`, then `playlist open`), and it played next. Queue order was correct. |
| 3 | `cmd:add` to the last queued track about 7 s before its end | Worked: the appended track played on without stopping. |
| 3b | `cmd:add` to the last queued track about 1.2 s before its end | Worked: transition at the end with `mode` staying `play`. |
| 4 | `playlistcontrol` with many `track_id`s in one call | 200, 300, 450, 600 and 800 ids worked (body up to about 6.5 KB). **1000 ids (about 8 KB) and 5000 failed** with a connection error. The limit looks like about 8 KB of request body. |

Conclusions:
- **Events work well.** `listen 1` on port 9090 pushes `playlist newsong <title> <index>` at the boundary, plus `playlist open <url>` (next track queued), `playlist stop`, `time`, `prefset … currentSong`, `playlist load_done`, and `playlistcontrol …` / `playlist addtracks|inserttracks listRef ARRAY(0x…)` for queue edits. Events are percent-encoded. Some are duplicated (two `playlist open`/`newsong` lines), so de-duplicate. The `listRef ARRAY(…)` text is useless; re-query `status` after a queue event.
- **A manual jump or seek emits** `playlist index`, `playlist stop`, `playlist open`, `playlist newsong` and `time`, and the same pair repeats about 0.5 s later. The UI should debounce events.
- **Gapless-safe queue edits** (as far as these tests show): append, insert next and reorder-after-current all behaved. The forum failure mode (appending after the last track is buffered) did not reproduce, but only 2 trials, both with short tracks.
- **Chunk size:** keep `playlistcontrol` requests at 400 ids or less (about 3.5 KB) to stay well under the 8 KB boundary. Bigger queues need several calls. Appends are in order, so chunks keep order.
- **Position after a boundary:** the first `time` after `newsong` was 0.5 s (FLAC) and 1.3 s (MP3), so `time` right after a transition is not reliable to the tenth of a second; use the event timestamp as 0.
- `time` is reported in real seconds and advances about 0.45 s per 0.45 s poll, so linear interpolation between polls is sufficient for the playhead.

## 8b. Implemented (Step 2)
The proposal above is now `src/Clims.Lms` (see its README and `docs/sprint.md` log). Differences from the plan and facts learned while building it:
- Typed layer over a raw layer, as proposed. Hand-written mapping from `JsonElement` replaces source-generated serializers (loose typing; no reflection). No `ILogger` yet.
- **`albums` tag `j` = `artwork_track_id`**, so cover URLs come straight from the album list. Album tag letters differ from track tags (`j`/`J`, `S`/`s`); the library has separate `AlbumTags` and `TrackTags`.
- `songinfo` replies with one single-key object per field (`songinfo_loop:[{"id":..},{"title":..},...]`).
- `search` loops are `contributors_loop`/`albums_loop`/`tracks_loop` with `*_id` keys and a `*_count` per group.
- A closed connection without a reply is `HttpRequestError.ResponseEnded` in .NET; verified with a real socket. Mapped to `LmsProtocolException`.
- Insert-next with several id chunks is sent last-to-first so order is preserved.
- Fixtures recorded from the real server live in `tests/Clims.Lms.Tests/Fixtures`.

## 9. Still open
1. Audio-level gaplessness (needs the Squeezelite `-v` capture, section 4b/4c).
2. HTTP Basic auth behaviour with a password set (not tested: needs changing server settings).
3. Appending after the last track with long FLAC tracks and larger buffers, plus repeating the append tests a few more times, to be sure the forum failure mode is gone.
4. Whether `playlistcontrol cmd:load` at a track boundary is audibly gapped (expected yes).
5. The exact cause of the 8 KB limit (LMS HTTP server vs. request line) and whether the CLI socket accepts longer lines.
6. Solution layout and naming (`src/Clims.Lms`, `src/Clims.Tui`, `tests/`).
1. Does HTTP auth apply to `/jsonrpc.js` when a password is set? Test with Settings → Security.
2. Does the telnet CLI need `login` on your setup, and do `playlist newsong` events arrive promptly at gapless boundaries?
3. Effect of `insert`/`delete` near the current track on gapless (needs an audible test).
4. How many `track_id`s one `playlistcontrol` call accepts.
5. Library change detection: `info total songs ?`, `rescanprogress`, or the `library changed` notification.
6. Solution layout and naming (`src/Clims.Lms`, `src/Clims.Tui`, `tests/`).
