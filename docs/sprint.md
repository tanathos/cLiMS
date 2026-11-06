# cLiMS – Sprint plan and development log

Living document. Update the **Status** column and the **Log** at the end of every working session. Agents: read sections 0 and 1, then the step you are working on, then the linked research notes.

## 0. Project summary
cLiMS is a full-screen terminal UI (.NET 10) client for a Lyrion Music Server (LMS 9.1.1) at `http://192.168.1.20:9000` (piCorePlayer, one player: Squeezelite `v2.0.0-1476-pCP`, MAC `b8:27:eb:a9:77:8f`, FLAC and MP3 library of 19 948 tracks / 2 008 albums / 1 004 artists / 195 genres). It browses the library, searches almost instantly, plays tracks and albums, manages custom queues, shows cover art and a waveform, and must never break gapless playback.

Research notes (read before coding; all measured on the real server unless marked inferred):
| File | Content |
|---|---|
| `docs/research.md` | Interfaces, framework comparison, waveform/live visualiser options, gapless rules |
| `docs/jsonrpc-client.md` | Protocol facts, quirks, command inventory, library shape, live player tests |
| `docs/ui.md` | Layout, feasibility, cover rendering, interaction sketch |
| `docs/search.md` | Latency measurements, local index design, sync states, scan behaviour |

## 1. Ground rules for every step
**Code**
- Solution layout (proposed): `src/Clims.Lms` (library, no UI deps), `src/Clims.Tui` (app), `tests/Clims.Lms.Tests`, `tests/Clims.Tui.Tests`. Target `net10.0`, nullable enabled, warnings as errors, `ImplicitUsings` on, file-scoped namespaces.
- Match surrounding code style once it exists; keep comments sparse and explain *why*.
- UI code never talks HTTP/JSON directly; it goes through interfaces from `Clims.Lms` (and the catalogue/search services built on them) so it can run on fake data.
- Everything async with `CancellationToken`; the UI thread never blocks on I/O.

**Server safety (this is the user's real, in-use player)**
- Default tests and tools must be **read-only** against the server. Anything that changes state (playback, queue, volume, scans, prefs) needs an explicit opt-in flag and the user's approval for the session.
- Before any state-changing test: snapshot (mode, queue ids, index, elapsed, volume, repeat, shuffle, mute), restore afterwards, tell the user. The player may be playing something else at any time; do not assume its state.
- Never start a library scan from code except as an explicit user action (7 min, disconnects the player).
- Do not expose ports 9000/9090 beyond the LAN. Server auth is currently off; support optional HTTP Basic credentials.

**Protocol facts every agent must know** (details in `jsonrpc-client.md`)
- JSON-RPC: `POST /jsonrpc.js`, `{"id":1,"method":"slim.request","params":[playerId|"", [cmd, start, count, "tag:value"...]]}`. `id` is optional.
- Errors are not reported: bad command/player/array body gives an empty reply or closed connection. No batching.
- Loose typing: numbers sometimes arrive as strings; empty results omit the `*_loop` key (`{"count":0}` or `{}`).
- Paging: `titles` max about 5 000 per call (use 2 000); `albums`/`artists`/`genres` return all in one call. `sort:changed` is capped at 100.
- Events: telnet CLI on 9090, `listen 1`; replies percent-encoded; ignore noise (`material-skin`, `menustatus`, `prefset`, `ARRAY(0x…)`); duplicates are normal; no login needed.
- Gapless: never stop/restart to advance; edit the live queue with `playlistcontrol` add/insert/delete/move; chunk id lists at 400 or fewer (about 8 KB body limit); `cmd:load` replaces the queue (not gapless).
- Server search is word-prefix only; accents folded. Local search must be better.

## 2. Status board
| Step | Goal | Status | Owner | Notes |
|---|---|---|---|---|
| 1 | TUI skeleton, fake data, "feel" test | Not started | | Framework go/no-go decision |
| 2 | `Clims.Lms` library | Not started | | |
| 3 | Catalogue navigation (index + Library tab) | Not started | | Needs 1 and 2 |
| 4 | Search | Not started | | Needs 3 |
| 5 | Playback, queues, waveform, polish | Not started | | Needs 2 and 3 |

Status values: Not started / In progress / Blocked / Done. Steps 1 and 2 can run in parallel.

## 3. Open decisions (defaults apply until the user answers)
| # | Question | Default |
|---|---|---|
| D1 | Queue tab: where do custom queues live? | Third tab "Queue" next to Search and Library |
| D2 | Left-pane dimensions in Library | Album Artist (default), Artist, Genre, Year, Folder; switch via header drop-down |
| D3 | Terminal in use / image strategy | Windows Terminal; half-block covers in lists, Sixel only for footer cover (optional) |
| D4 | Multiple players | One player (the Pi); no selector yet, but code takes a player id |
| D5 | Footer height | 5 to 6 rows |
| D6 | Terminal.Gui v2 stability on .NET 10 | Decided by the Step 1 spike |
| D7 | Image decoder for covers (ImageSharp vs SkiaSharp) | Evaluate in Step 3; check licence |
| D8 | Live waveform (Squeezelite `-v` + vismux) | Out of scope until static waveform works |

Decided: SQLite on-disk index (memory-only thumbnails); search covers artists, albums, tracks and genres (not composers); header shows sync state; resync is never automatic (ask first, "Later" asks again at next launch); sync results are mixed with server results while syncing.

---

## Step 1 – TUI skeleton (feel test)
**Goal:** a runnable full-screen app with the final layout, fake data and no network, to judge how it feels and to choose or reject the framework.

**Scope**
- Project `Clims.Tui` using **Terminal.Gui v2** (check the latest NuGet version and its status first; the README has recommended Alpha for new projects, sources conflict about stable vs beta; pin the exact version).
- Always full screen (alternate buffer), resize-aware, minimum size about 100x30 (below that, collapse cover/waveform, never the controls).
- Header: app name, search box (focus with `/`), player name + **sync status** text (`Not synced`, `Sync in progress, live search only`, `Synced`; fake value, switchable by a debug key), volume.
- Tab strip: Search, Library (and Queue placeholder per D1).
- Library tab: left list (about 1 000 fake artists, type-ahead, A-Z jump), right pane of album blocks (cover, title, year · genres, duration, two-column tracklist as in the MusicBee reference).
- Search tab: grouped Artists / Albums / Tracks with fake data (top 2/4/10, "More"); typing in the header switches to this tab.
- Footer: top progress bar (click to seek), left cover + fake waveform, buttons Prev / -10 s / Play-Pause / +10 s / Next, volume. Fake clock ticking at about 10 fps.
- Data via interfaces (`ILibrarySource`, `IPlayerController`) implemented by in-memory fakes, to be replaced in Steps 3 to 5.

**Technical notes**
- Covers in lists: draw as true-colour half-block cells (`▀`, two pixels per cell). Sixel is unsuitable for scrolling lists (no clipping, re-sent on redraw, flicker risk, Windows Terminal emulates a 10x20 virtual cell). Optional: Sixel for the single footer cover through `ImageView`/`SixelEncoder`/`SixelToRender` (only when the terminal supports it).
- Fake images: generate gradients/noise or ship a few small JPEG/PNG; the real covers come from `/music/<artwork_track_id>/cover_96x96_o.jpg` (server-side resize, about 6 KB, about 45 ms).
- Custom views are needed for: album blocks (variable height, virtualised), two-column track lists, progress/seek bar, waveform strip. Plan virtualisation from the start (2 008 albums, 19 948 tracks).
- Use mouse and keyboard from day one: keys `/` search, `1`/`2`/`3` or Tab for tabs, Space play/pause, `n`/`p` next/prev, left/right seek 10 s, `+`/`-` volume, Ctrl+Q quit.
- Dark theme with true colour, inspired by the MusicBee screenshot.

**Feel checklist (record results in the Log)**
1. Startup time, flicker on resize, redraw cost at 10 fps in the footer.
2. Scrolling 2 000 fake albums with half-block covers: smooth or not (target: no visible lag).
3. Mouse support: click tabs, seek bar, buttons; wheel scroll.
4. Key handling: does `/` or Space conflict with text input focus?
5. Unicode: box-drawing, half blocks, braille waveform, emoji width in Windows Terminal.
6. Sixel cover in the footer in Windows Terminal (works? aligned? flicker?).
7. Go/no-go: if the framework fails the feel test, evaluate the fallbacks (SharpConsoleUI on Spectre, Consolonia, or Spectre.Console only for output) and record why.

**Definition of done:** app runs with `dotnet run`, all regions visible, fake interactions work, feel checklist filled in, framework decision recorded (D6), README with run instructions.

**Risks:** Terminal.Gui v2 API churn; ListView virtualisation limits; cell/pixel aspect of half-block covers in Windows Terminal.

---

## Step 2 – `Clims.Lms` library
**Goal:** a separate, UI-free `net10.0` library that wraps LMS: JSON-RPC requests, typed models, paging, events, URL helpers.

**Structure** (see `jsonrpc-client.md` section 3)
- `Transport`: `LmsRpcClient` (raw `RequestAsync(playerId, params object[])`), `LmsClientOptions` (BaseUri, optional Basic credentials, timeout, retry). Take `HttpClient` by injection; optional `ILogger`.
- `Commands` / `Models`: Album, Artist, Genre, Track, SavedPlaylist, PlayerInfo, PlayerStatus, QueueItem, `Page<T>`; a `TagSet` flags enum that produces `tags:...` strings.
- `Services`: `ILibraryApi`, `IPlayerApi` (transport, volume, seek), `IQueueApi`, `IServerApi` (version, serverstatus, scan state, counts, `lastscan`), facade `ILmsClient`.
- `Paging`: `IAsyncEnumerable<T>` helpers over start/count.
- `Events`: `ILmsEventStream` on TCP 9090 (`listen 1`): reconnect with back-off, percent-decode, de-duplicate, debounce, parse the useful events (`playlist newsong|stop|pause|open`, `mixer volume`, `pause`, `play`, `power`, `rescan done`, `library changed`, `client disconnect|reconnect`, queue edit events), drop noise. Fallback `StatusPoller`.
- `Art`: URL builders for cover art (`/music/<id>/cover_WxH_o.jpg`) and audio download (`/music/<id>/download`).
- `Json`: source-generated `System.Text.Json` context; tolerant converters (string-or-number, missing loop key as empty list, doubles for `time`/`duration`/`playlist_timestamp`).

**Technical notes**
- Map empty reply / connection reset to `LmsProtocolException` (include command and player); network failures to `LmsConnectionException`. Validate player id against `players` and use `can <cmd> ?` for capability checks at startup.
- Limit concurrent requests (2 to 4); LMS is mostly single-threaded and gets slow during scans (poll timeouts of 30 s were seen). Retry only idempotent reads. During a scan (`rescanprogress` `rescan:1`) back off.
- Needed commands: `serverstatus`, `players`, `artists`, `albums`, `titles`, `genres`, `search`, `songinfo`, `status`, `playlistcontrol`, `playlist index|move|delete|clear|repeat|shuffle`, `pause`, `play`, `stop`, `time`, `mixer volume`, `power`, `info total ...`, `rescanprogress`, `can`, `version`. Full list in `jsonrpc-client.md` section 2.
- Tag letters that matter: tracks `a l t d y u K J c e s p g i q r T I Q`, albums `l y j t i q w W a S s X`; `id` and `title` always present. Keep default sets small per view (replies are large).
- `playlistcontrol cmd:add|insert|load|delete track_id:a,b,c` takes ordered id lists: chunk to 400 or fewer ids (about 8 KB request body limit; 1 000 ids failed).
- Elapsed time: `PlayerStatus.Elapsed` plus `ReceivedAt`; right after a track change `time` is only accurate to about a second, treat the `newsong` event as time 0.
- IDs can change after a full rescan: use `url` (file path) as durable key for stored queues.

**Tests**
- Unit tests with a fake `HttpMessageHandler` and fixtures recorded from the real server (save samples of `serverstatus`, `albums`, `titles`, `status`, `search`, empty results, odd typing).
- Optional integration tests against the real server, **read-only by default**, enabled by an environment variable (for example `CLIMS_LMS_URL`); mutating tests behind a second flag and the snapshot/restore helper.

**Definition of done:** library builds without UI references, covers the command inventory, typed models parse the fixtures, event stream handles reconnects and noise, tests pass, XML docs on public API, a short `src/Clims.Lms/README.md`.

**Risks:** poor server error reporting; loose JSON types; events delivered in duplicates; slow server during scans.

---

## Step 3 – Catalogue navigation (index + Library tab)
**Goal:** the Library tab backed by a local SQLite index, with sync status in the header.

**Scope**
- `Clims.Tui` (or a `Clims.Catalog` project) gets a **catalogue service**: SQLite persistence (`Microsoft.Data.Sqlite`), in-memory model, sync service with a state machine.
- Tables: `artists`, `albums`, `tracks`, `genres`, `meta` (`lastscan`, `song/album/artist/genre counts`, `server_uuid`, `schema_version`, `synced_at`). Cache file per server (key: `uuid` from `serverstatus`) in the per-user app data folder; safe to delete.
- Sync order: genres, artists, albums, then tracks with a light tag set (about 2.5 MB, about 1 minute; use pages of 2 000). Show progress. Pause or back off while the server is scanning.
- **Never resync automatically.** First build with no index asks once. When `rescan done` or startup shows a changed `lastscan` or changed counts, status becomes `Server changed, resync pending` and the user is asked (Resync now / Later; Later asks again at next launch). A `rescan done` with no change must not prompt.
- Status states and header text: `Not synced`, `Sync in progress, live search only` (with progress), `Synced` (age), `Server changed, resync pending`, `Offline (using local index)`.
- Library tab: left pane dimension list (D2) with A-Z jump from `textkey`; right pane artist heading and album blocks (cover, title, year · genres, duration, tracklist in two columns). **No "Album and Tracks" title bar and no year selector** (removed by the user).
- Album blocks: when shown, re-fetch that album's tracks live (`titles album_id:N`, about 100 ms) and update the index row (self-healing after targeted rescans, which only change `lastUpdated`).
- Covers: lazy fetch when a block becomes visible, memory-only LRU of converted half-block cell arrays, 2 to 3 concurrent requests, cancel when scrolled away. No disk cache.
- Actions on artist/album/track: Enter (play: replaces queue, not gapless), `a` add to end, `n` play next, Space multi-select. (Playback wiring lands in Step 5; Step 3 may stub with log messages.)

**Technical notes**
- Cost measured on the server: artists 1.4 s, albums 6 to 8 s, tracks about 56 s with tags `alts`, about 133 s with a rich set. Prefer light sets; fetch details on demand.
- No server-side "changed since" filter exists; `albums sort:changed` is capped at 100 and did not reorder after a targeted rescan; track `lastUpdated` (`U`) is the only reliable per-item marker. Section 9 of `search.md`.
- `lastscan` did not change after a no-change full scan; whether it changes after real changes is unconfirmed. Count changes are the fallback trigger.
- During a scan the server disconnects/reconnects players at the end; handle `client disconnect|reconnect`.
- Album ordering: by year then title for an artist; consider compilations (`compilation:1`) and "Various Artists" grouping (decision needed).
- Image decoding: the server returns JPEG/PNG; pick a decoder (D7) and convert to a small RGB grid for half-block drawing; request the smallest useful size (for example 48x48 or 96x96).

**Definition of done:** index builds, persists and reloads; header status correct in all states; Library tab navigates artists to albums to tracks from the index with lazy covers; resync prompt works; on-view refresh works; tests for the sync state machine and the SQLite layer.

**Risks:** first-run time on a slow server; virtualised variable-height list; decoder licensing; stale data between scans.

---

## Step 4 – Search
**Goal:** almost-instant search while typing, grouped as in the reference screenshot.

**Scope**
- Typing in the header switches to the Search tab and updates results per keystroke. Groups: **Artists, Albums, Tracks, Genres** (no composers). Show counts (`Tracks (10 / 29)`) and a "More" action; default top 2 artists, 4 albums, 10 tracks (tune after seeing it).
- Local engine over the in-memory index: normalised strings (case and diacritics folded), prefix and substring matching (the server only matches word prefixes), own ranking (exact > word-prefix > contains; artist boost), multi-word AND in any order. Expected under 5 ms for 20 k rows.
- Mixed sources while syncing: groups not yet loaded come from the server `search` command (debounce about 120 ms, cancel stale requests), loaded groups from the local index, in one screen.
- Keyboard: arrows move between groups and rows, Enter plays, `a`/`n` add/next, Esc clears and returns to the previous tab.
- Result thumbnails: lazy, memory LRU, from `cover_48x48_o.png`. Artist rows use an icon (LMS has no artist portraits).

**Technical notes**
- Server search timings (for the fallback): `iron` about 100 ms, `the` about 440 ms, `i` about 250 ms. Global `search` reply keys: `contributors_loop` (artists), `albums_loop`, `tracks_loop`, plus `*_count` and `count`. Mid-word queries return nothing server side (`aiden` finds nothing).
- Server queries accept `tags:` and give counts; use them for the "10 / 29" headers.
- Handle out-of-order results: tag each request with a sequence number and drop stale responses.
- Highlight matched fragments in results.

**Definition of done:** local search under 50 ms on the real index, server fallback works during sync and offline mode shows local results, group layout matches the reference, tests for normalisation, ranking and stale-response handling.

**Risks:** ranking quality; result list virtualisation; layout of thumbnails in text mode.

---

## Step 5 – Playback, queue, waveform and everything missing
**Goal:** make it a real player client, gapless-safe, with custom queues.

**Scope**
- **Playback state:** `status` polling at about 1 s plus the event stream; linear interpolation of the elapsed time between polls for the footer (about 10 fps UI); handle `playlist newsong` (time 0), pause, power, mixer volume, `client disconnect|reconnect`.
- **Footer controls:** Prev (`playlist index -1`), -10 s (`time -10`), Play/Pause (`pause`), +10 s (`time +10`), Next (`playlist index +1`), volume (`mixer volume`), seek by clicking the progress bar (`time <secs>`).
- **Queue tab / custom queues** (D1): local store of named queues keyed by track `url`; operations map to LMS as in `jsonrpc-client.md` section 5 (append: `cmd:add`; play next: `cmd:insert`; replace upcoming while keeping the current track; reorder: `playlist move`). Explicit "Play now" uses `cmd:load` (not gapless; confirm in the UI). Chunk id lists at 400 or fewer. Re-resolve ids from urls after rescans.
- **Gapless rules** (`research.md` section 4c): never stop to advance; edit in place; keep at least 2 tracks ahead; after an append check that playback continues and use `playlist index +1` only if it stopped with tracks left; do not change crossfade, ReplayGain or sync at runtime.
- **Waveform (static first):** download the current track (`/music/<id>/download`), decode in .NET, compute min/max peaks (about 1 to 2 k points), cache in memory (and optionally on disk), render with braille/half-block characters in the footer, playhead from interpolated elapsed time. Pre-fetch the next queue item. Show a spinner while decoding. Live layer (Squeezelite `-v` + vismux UDP) stays optional (D8).
- **Missing pieces / polish:** configuration file (server URL, credentials, player id, theme, keybindings), first-run setup, logging to file (no console output in TUI mode), error toasts and offline mode, help overlay (`?`), resize handling, packaging (single-file publish or `dotnet tool`), README and user docs, CI build and tests, accessibility (no colour-only information), graceful shutdown (close sockets, save state).

**Technical notes**
- Measured transitions: natural FLAC to FLAC advance: `newsong` event within 0.05 s of the track end, no `time`/`mode` stall; next track is pre-opened about 1 s after the current starts; mid-queue and end-of-queue appends worked; inserting right after the current track re-opened the next stream correctly.
- Audio-level gaplessness is still unverified (the tests only watched control-plane signals). To check later: Squeezelite `-v` PCM capture and look for zero runs at the boundary.
- First `time` after a track change is accurate only to about a second.
- Player state can change behind the app's back (other clients, the user); always re-query `status` after events and never assume.
- Mutating tests: snapshot/restore (see section 1), mute the player (`mixer muting`) during tests, restore mute afterwards.

**Definition of done:** play/pause/seek/next/prev/volume work from the footer and keyboard; queues can be created, edited, saved, appended and played; an album plays gapless in a manual listening test; waveform with playhead shown for the current track; config, logging and packaging in place; docs updated.

**Risks:** gap on `cmd:load`/jumps (expected), decode time for large FLACs, event noise, player reconnects during use.

---

## 4. Log
| Date | Who | Entry |
|---|---|---|
| 2026-11-06 | Claude | Research phase done (docs listed in section 0). Sprint plan created. No code yet. |

## 5. Changelog of decisions
- UI: "Album and Tracks" title bar and year selector removed from the Library right pane.
- Search: SQLite index on disk; thumbnails in memory only; genres in, composers out; header sync status; mixed local/server results while syncing; resync asks first, "Later" asks again at next launch.
- Scan test: `rescan done` fires even with no change; `lastscan` unchanged after a no-change scan; scans take about 7 minutes and reconnect players.
