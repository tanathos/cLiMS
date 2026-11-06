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
| 1 | TUI skeleton, fake data, "feel" test | Done, awaiting user's feel review | Claude | Terminal.Gui 2.5.0 go (D6); see Log for checklist results and what is untested |
| 2 | `Clims.Lms` library | Done | Claude | 190 tests (read-only integration run against the real server); mutating tests written but not run, see `src/Clims.Lms/README.md` |
| 3 | Catalogue navigation (index + Library tab) | Done | Claude | 135 new tests; first real sync about 1.5 min; see Log for findings and what is deferred |
| 4 | Search | Done | Claude | 37 new tests; local search about 2 ms (worst 15 ms) on the real 19 960-track index; server fallback verified with no index; see Log |
| 5 | Playback, queues, waveform, polish | Done, awaiting your listening test | Claude | 40 new tests; real status, queue and waveform verified read-only against your player; transport commands covered by fake-server tests only; see Log |

Status values: Not started / In progress / Blocked / Done. Steps 1 and 2 can run in parallel.

## 3. Open decisions (defaults apply until the user answers)
| # | Question | Default |
|---|---|---|
| D1 | Queue tab: where do custom queues live? | Third tab "Queue" next to Search and Library |
| D2 | Left-pane dimensions in Library | **Implemented: Album Artist (default), Genre, Year**; switch with F6 or a click on the pane header. Artist (all contributors) and Folder not offered |
| D3 | Terminal in use / image strategy | Windows Terminal; half-block covers in lists, Sixel only for footer cover (optional) |
| D4 | Multiple players | One player (the Pi); no selector yet, but code takes a player id |
| D5 | Footer height | 5 to 6 rows |
| D6 | Terminal.Gui v2 stability on .NET 10 | **Decided: go.** 2.5.0 stable works on .NET 10 for everything in the Step 1 layout; no fallback needed (final call stays with the user's feel review) |
| D7 | Image decoder for covers (ImageSharp vs SkiaSharp) | **Decided: SkiaSharp 4.154.0 (MIT).** Decodes JPEG/PNG and resamples (Mitchell) on .NET 10 with no extra setup; ImageSharp's split licence avoided |
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
| 2026-11-06 | Claude | **Step 1 implemented** (`src/Clims.Tui`, `cLiMS.slnx`, README). Terminal.Gui 2.5.0 pinned. Full layout with fake data: header + search + sync status, tab strip, Library (artist list with type-ahead + virtualised album blocks), Search (grouped, "More…"), footer (seek, cover, waveform, transport, volume), 10 fps fake clock. Tested by driving a real Windows Terminal tab (SendKeys, mouse_event, screenshots). Results below. |
| 2026-11-06 | Claude | **Step 1 fixes after user review:** (1) scrollable panes (artist list, album pane, search results) now have a scrollbar column with click-to-jump; (2) mouse wheel works on the hovered pane without clicking first (root cause: `RowList.Paint` re-revealed the selected row on every frame and undid the scroll; now it only reveals after the selection changes); (3) header is 3 rows tall (blank row above and below, 2 columns of side padding, own darker background); (4) resize crash fixed (see below). |
| 2026-11-06 | Claude | **Real covers spike:** `--lms <url>` loads 10 random album covers from the live server (`albums ... sort:random`, then a track id of the album, then `/music/<trackId>/cover_192x192_o.jpg`, about 250 ms each) and maps every fake album onto them. Decoded and resampled with SkiaSharp 4.154.0 (MIT) using Mitchell cubic to the cell grid (16x16 px list cover, 10x10 px footer). Findings: SkiaSharp works on .NET 10 with no extra setup (**leans D7 towards SkiaSharp**, still to confirm in Step 3); covers are recognisable but coarse at 16x16 px, so a larger list cover (e.g. 24x12 cells) would look noticeably better; the `albums` command has no artwork tag we could get (tags `J`/`K`/`c` returned nothing), so the cover is addressed via a track id of the album. Throwaway code: `Data/DevCovers.cs`, `Ui/CoverImage.cs`. |
| 2026-11-06 | Claude | **Cover resolution experiment** (F3 mode, F4 size). Samples per cell: half-block `▀` 1x2, quadrant `▚` 2x2, braille 2x4, each with only 2 colours per cell. Compared on real covers in Windows Terminal: **quadrant at 24x12 cells is the best cell-based option** (now the default; about 4x the samples of the original 16x8 half-block); braille adds little detail and a dotty texture, so not recommended; half-block 16x8 is the grainy baseline. Cell-based covers stay pixelated by nature; the only way to real sharpness is **Sixel** (true pixels, about 160x160 px for a 16x8 cell cover at WT's 10x20 virtual cell), which still has the scrolling/clipping drawbacks recorded in `ui.md` section 3. Quadrant at 24x12 was still judged too grainy, so Sixel was tried next (see next row). |
| 2026-11-06 | Claude | **Sixel covers implemented and working** in Windows Terminal (default `F3` mode when `--lms` covers are loaded). Covers are fetched at 300 px, resampled to 10x20 px per cell (WT's virtual cell), so a 24x12-cell list cover is 240x240 real pixels and the footer cover about 100x94 px; the result is dramatically sharper than any cell mode. Implementation: `Ui/SixelCovers.cs` publishes per-view placements to `driver.GetSixels()`; `PaintView.DrawCover` uses Sixel only for fully visible covers (partly scrolled covers fall back to quadrant cells, since a sixel cannot be clipped); encoded data cached per cover/size; `AvoidBottomScroll` for images touching the last row. Findings: (1) Terminal.Gui reports Sixel support about 2.4 s after start (needs a repaint then); (2) repainting any cell under a sixel erases it, so lists resend on every paint and the footer (repainting at 10 fps) resends every frame, **possible flicker, to be judged by the user**; (3) hidden views must withdraw their images (done on tab switch); (4) 120 random-size resizes with Sixel active: no crash; (5) digits are type-ahead while the artist list has focus, so `1`-`3` need another pane focused. Open: footer flicker at 10 fps, cost of Sixel on slower terminals, behaviour in terminals without Sixel (falls back to quadrants, untested), search-result thumbnails (Step 4) and queue covers (Step 5) would need the same placement logic. |
| 2026-11-06 | Claude | **Step 2 done: `src/Clims.Lms` + `tests/Clims.Lms.Tests`** (net10.0, nullable, warnings as errors, XML docs, AOT-compatible, no dependencies). Transport with error mapping (empty reply / closed connection = `LmsProtocolException`, unreachable/timeout = `LmsConnectionException`, 401/403 = `LmsAuthenticationException`), concurrency cap, retries for reads only; typed Library/Player/Queue/Server APIs; `TrackTags`/`AlbumTags`; paging; `CliEventStream` (reconnect, dedupe, noise filter) plus `StatusPoller` fallback; art/download URL builders. **190 tests pass** (unit tests over a fake handler with fixtures recorded from the real server, real-socket tests for hang-up/refused/event stream, 11 read-only integration tests run against the live server; the one mutating test is skipped unless opted in and was **not run**). Findings and decisions: (1) on `albums`, tag **`j` returns `artwork_track_id`**, so cover URLs need no extra `titles` call (the Step 1 `DevCovers` spike used a track id; replace it with this); tags differ between albums and tracks (`j`/`J`, `S`/`s`), hence two enums; (2) `songinfo` returns one single-key object per field, merged by `Track.FromSongInfo`; (3) `playlistcontrol cmd:insert` with several chunks must be sent last-to-first to keep order; (4) a hang-up without HTTP reply maps to `HttpRequestError.ResponseEnded`, verified with a real socket; (5) replies are mapped by hand from `JsonElement` instead of source-generated serializers (shapes too loose, no reflection needed), a deliberate deviation from the plan; (6) no `ILogger` yet (add when needed); (7) scan back-off is left to callers via `GetScanStateAsync` (documented); (8) `PlayerStatus.ElapsedAt` interpolation guards against clock skew (a unit test found this bug). Not covered: Basic-auth against a password-protected server, `playlist` saved-playlist editing, `sync`/`syncgroups`, preferences, `musicfolder`, `years`/`roles` listings (raw `Rpc.RequestAsync` covers them). |
| 2026-11-06 | Claude | **Step 3 done: `src/Clims.Catalog` (new), Library tab on real data, covers, sync status.** `Clims.Catalog` (UI-free, depends on `Clims.Lms` + `Microsoft.Data.Sqlite`): in-memory `Catalog` read model (artists, genres, albums, tracks per album, summaries, genre and year indexes, simple word-prefix search), `SqliteCatalogStore` behind `ICatalogStore` (one file per server UUID in `%LOCALAPPDATA%\cLiMS`, atomic save in one transaction, schema version, damaged file = no index), and `CatalogService` with the state machine (NotSynced, Syncing, Synced, ServerChanged, Offline, plus Demo). **Rules implemented and tested:** it never syncs by itself; first run asks once; `rescan done` with no change does not prompt; a moved `lastscan` or changed counts give `ServerChanged` and one prompt; "Later" means no second prompt this session and a prompt again next launch; cancel or failure keeps the old index and leaves the store untouched; the server being scanned makes the sync wait; a first build is browsable as soon as artists and albums are in (partial catalogue in memory, tracks stream in, the on-view refresh fills visible albums meanwhile). TUI: Library tab reads the catalogue (dimensions Album Artist / Genre / Year on F6 or by clicking the header, type-ahead, album blocks with real tracks, durations and genres), lazy cover fetching (`LmsCoverSource`: 3 concurrent, cancelled when scrolled away, memory LRU of 300, failure back-off), on-view album refresh (`AlbumRefresher`, 2 concurrent), header shows the sync state with progress and age, prompt and notices on the tab strip with clickable answers, F5 / click on the status to resync or cancel, `a` / `n` stubbed until Step 5, search tab runs on the in-memory catalogue. Demo mode (`--demo`, or no address) keeps the fake library. **Findings:** (1) **LMS 9.1.1 drops the connection for `titles` with the genre tag unless an explicit `sort:albumtrack` is given** (`sort:title` and no sort both fail, `alts` without genre works); found by the real-server integration test, pinned by a unit test; about 6.8 s per 2 000 tracks, so a full sync takes about 1.5 minutes on this Pi; (2) `artists` also returns people who only appear on tracks, so the Album Artist dimension lists only artists with albums; (3) the server's counts already differ from the Step 2 research (19 960 tracks, 2 009 albums): the library changed, so the change detection will fire for real; (4) a cache hit starts in under 4 s and shows covers right away; (5) with an unreachable server the Offline state appears after about 7 s on Windows (2 s per refused connect, 3 attempts); (6) SkiaSharp throws instead of returning null for non-image bytes (wrapped; a unit test caught that failures were skipping the back-off); (7) two cancellation races (a freed slot starting an album/cover that just scrolled away) found by repeated test runs and fixed by re-checking after acquiring the slot; (8) `--lms` is only remembered after the server was actually reached. **Tests:** Catalog 90 (+2 opt-in real-server sync tests that passed: counts match, SQLite roundtrip, second launch asks nothing), Tui 45 (status text, address parsing, prompt, cover source, refresher), Lms unchanged. **Not done / deferred:** Space multi-select (arrives with the queue, Step 5), Folder dimension, thumbnail-free Offline browsing of covers (covers need the server), resync of a single changed album from the server events, A-Z rail (type-ahead covers the jump), search ranking and server fallback (Step 4). |
| 2026-11-06 | Claude | **Step 4 implemented** (ranked search). `Clims.Catalog`: `SearchQuery`, `SearchRanking` (tiers: exact 100, starts with 80, every word starts a word 60, words anywhere 30; artist-only matches for albums and tracks score half; a title match whose artist also matches gets +5; ties: shorter name, then alphabetical), `Catalog.Search` returns `RankedSearchResult` (artists, albums, tracks, genres, each with its total), `TextFolding.Highlights`, and `SearchService` (local answer synchronously on every keystroke; the server fills the groups the index lacks, debounced 120 ms, previous request cancelled, stale answers dropped by sequence number, an outage keeps the local result and adds a note). `Clims.Tui`: new `ResultsView` (two-line rows with a 4x2 thumbnail, highlighted matches, variable-height virtualised list), `SearchTab` rewritten (Artists 2, Albums 4, Tracks 10, Genres 3, headers `Tracks (10 / 220)`, "More…", `a` / `n`, Enter plays or opens artist/genre in Library, Esc clears and returns to the previous tab). Measured on the real saved index (opt-in test `CLIMS_CATALOG_DB`): `iron` 1.8 ms, `the` 7 ms, single letters 13-15 ms. Verified on the real server in a terminal, with the index and with the index moved aside (everything from the server). Findings below. |

### Step 4 findings
1. **Local search is cheap enough to run on every keystroke**, so the 250 ms debounce is gone; only the server part waits (120 ms).
2. **Substring matching differs from the server on purpose:** `ure` finds The Cure locally, the server finds nothing. The server fallback is therefore a degraded mode (word starts only); ranking is applied to its hits too, so the order looks the same.
3. **LMS only sends an album title when asked with the album tag** (`l`): the first fallback screenshot showed albums without titles. Fixed and pinned by a test.
4. **Server tracks bring their own album title and artwork id** (`TrackExtras`), because during the first sync the index knows the album but not necessarily its tracks, and with no index it knows nothing.
5. **Thumbnails reuse the Library cover pipeline** (same cache, same size) instead of a separate 48x48 request; at 4x2 cells a Sixel is about 40x40 px, so the larger source costs little and avoids a second cache. Lazy fetch, 3 concurrent, cancelled when scrolled away, as before.
6. **Not done:** a track hit from the server whose album is not in the index cannot be played (notice); playing is still the fake player until Step 5; the server's genre search goes through the global `search` command (no per-genre filter exists).
| 2026-11-06 | Claude | **Step 5 implemented.** New `src/Clims.Waveform` (download, decode FLAC / MP3 / Ogg / WAV with BunLabs.NAudio.Flac, NLayer, NVorbis; peaks; service with prefetch, cancellation, retry, memory cache). `Clims.Tui`: `IPlayerController` extended (queue, connection problem, enqueue, jump, remove, move, clear, saved queues); `LmsPlayerController` (status polled every second and on CLI events, elapsed interpolated, commands serialised in order, queue re-read only when the server's queue timestamp or length changes, optimistic volume/seek/pause shown at once, "add" followed by a check that playback did not stop); `QueueTab` (live queue, saved queues by file URL in `queues.json`, name bar, confirmation before anything that restarts the stream); `HelpOverlay` (`?`); footer shows the real waveform with a visible "analysing" / "no waveform: reason" state; `a`/`n` queue the highlighted track, `A`/`N` the whole album; log file replaces the temp crash log; `--player`; single-file publish tried; `.github/workflows/ci.yml`. Findings below. |

### Step 5 findings
1. **The library is not all FLAC:** a track picked by chance was an MP3 (my first format sample missed `mp3` because of a regex without digits), so MP3 decoding was added; FLAC (the majority) decodes through the same path and the footer showed a real FLAC waveform for the queued track.
2. **Race found by repeated test runs:** a status poll already in flight could overwrite an optimistic volume or seek and make the control jump back. Polls now skip applying their answer while a command is outstanding or a local edit happened during the request; the poll that follows the command is applied. 12 consecutive clean runs afterwards.
3. **Disposed cancellation source** in the waveform service (cancel after the download finished) found by the first test run; fixed.
4. **Sixel images draw over overlays:** the help overlay showed the Library's covers on top of its text until the pages under it were hidden and their images released; closing it restores them.
5. **Hiding views moves keyboard focus** to the next focusable view (the search box ate the second `?`): the overlay takes focus while open.
6. **Not done / not verified:** audio gaplessness (needs a listening test; the control-plane rules are followed: edits in place, `Load` only on explicit play or replace, confirmation before replacing a queue); the transport commands against the real player (I did not interrupt your music: only read-only status, queue and download were used); password-protected servers in the app; multiple players (first connected hardware player or `--player`); the optional live visualiser (D8); configurable keybindings and themes; first-run setup screen (the address comes from `--lms`, the environment or the remembered value); the queue shows at most 500 entries; digit tab keys do not work while the left Library list has focus (letters are type-ahead there); a "More" or hint row in the Queue tab can be selected.

### Resize crash (found and fixed 2026-11-06)
- **Symptom:** app exited when the window was resized.
- **Cause:** `PaintView.Put` clipped text by UTF-16 index. Narrowing the window cut the header's `🔊` emoji (a surrogate pair) in half, and Terminal.Gui throws `ArgumentException: Only valid Unicode scalar values are allowed in a single Grapheme cluster` on a lone surrogate. Real track titles containing emoji would have hit the same bug.
- **Fix:** `Put` and `Clip` drop a split surrogate at the cut. `PaintView` also catches exceptions from `Paint()` and logs them instead of ending the app. `Program` logs any fatal exception.
- **Crash log:** `%TEMP%\clims-crash.log` (see `CrashLog.cs`).
- **How it was found:** a script launched the app in a Windows Terminal tab, optionally sent keys, then moved/resized that terminal window to random sizes (80 rounds per scenario via `MoveWindow`). Before the fix, 2 of 3 scenarios (Search tab with results, Library scrolled) crashed within 40 rounds; after the fix, 3 x 80 rounds ran clean with an empty crash log. Resize is therefore now **tested**; the below-100x30 footer collapse was exercised by the random sizes but not inspected visually.
- **Rule for future code:** never slice strings by index for display. Go through `PaintView.Put`/`Clip`, or use grapheme-aware APIs. Wide characters (emoji, CJK) still count as one column in our width maths; keep them out of layout-critical spots or handle width explicitly.

### Step 1 feel checklist results (Windows Terminal, 1920x1080, about 192x42 cells)
1. **Startup / redraw:** window up in under 4 s on first run (not precisely timed). Footer repaint at 10 fps costs avg 2.4 ms, header status 0.2 ms; idle CPU about 8 % of one core. Resize flicker not judged visually; resize stability tested, see "Resize crash" above.
2. **Scrolling 2 515 albums with half-block covers:** smooth in a stress run (about 120 PageDown presses): AlbumsView paint avg 6.7 ms, max 26 ms including JIT warm-up. Covers clip cleanly at top and bottom edges like text. Fake covers are procedural; real image decode cost is untested (Step 3).
3. **Mouse:** tab clicks, seek bar, Prev/Next/Play-Pause buttons, volume bar, artist click and wheel scroll all work. (-10s/+10s buttons share the same code path, not clicked individually.)
4. **Key conflicts:** `/` and Space are global only when the search box is not focused. Letters are type-ahead in the artist list, so `n`/`p`/`1-3` are disabled there (use the buttons, or Tab to another pane). The terminal sends `/` fine; no conflict with the TextField once focus starts in the artist list.
5. **Unicode:** box/half-block/block-eighths, `●`, `◀▶❚❚`, `━─░█` and the 🔊 emoji in the header render correctly and aligned in Windows Terminal. Braille waveform not used (block eighths look good).
6. **Sixel footer cover:** **not implemented or tested.** The half-block cover in the footer looks good enough; revisit only if wanted.
7. **Go/no-go:** **go** on Terminal.Gui v2.5.0; the stock widgets used were only `Window` and `TextField`, everything else is owner-drawn on `View`, which keeps the API-churn risk small.

**Untested:** visual check of the below-100x30 collapse (code exists in `MainWindow.OnTick`; random-size resize runs did not crash), other terminals (conhost, WSL), keyboard layouts other than the author's Italian one, dragging the scrollbar thumb (only click-to-jump is implemented).

**Gotchas found (also in README):** container `View`s need `CanFocus = true`; `key.Handled` set in `Keyboard.KeyDown` did not stop the same key being typed into a freshly focused TextField (fixed by starting focus in the list); `View.Activated`/`View.Frame` name clashes; `Force16Colors` must be set false on the driver or colours are quantised to 16; the headless/ANSI driver with redirected stdio printed nothing, so UI tests need a real console (see `drive.ps1`-style approach: launch in Windows Terminal, SendKeys, screenshot).

## 5. Changelog of decisions
- UI: "Album and Tracks" title bar and year selector removed from the Library right pane.
- Search: SQLite index on disk; thumbnails in memory only; genres in, composers out; header sync status; mixed local/server results while syncing; resync asks first, "Later" asks again at next launch.
- Step 3: Library dimensions Album Artist / Genre / Year (D2); SkiaSharp for covers (D7); sync status states as in `search.md` plus a Demo state; prompt and notices live on the tab strip row; the sync is staged so a first build is browsable early; F5 or a click on the status asks to resync or cancel; `--lms` address remembered only after contact; `Catalog` type aliased as `MusicCatalog` inside `Clims.Tui` (it shares its name with the namespace).
- Step 4: search runs locally on every keystroke (no debounce); the server only fills groups the index lacks; ranking tiers and the artist boost are defined in `SearchRanking`; search result rows are two lines with a 4x2 thumbnail that shares the Library's cover cache; Esc in the search returns to the tab you came from.
- Step 5: the player controls the first connected hardware player unless `--player` or `PlayerId` says otherwise; queue edits are in place (gapless), only play and replace restart the stream and ask first; waveforms come from the original file, decoded in .NET (no ffmpeg); saved queues are local, keyed by file URL.
- UI (Step 1 review): every scrollable pane shows a scrollbar in its last column and reacts to the wheel while hovered, focused or not; header is 3 rows with padding.
- Step 1: Terminal.Gui v2.5.0 chosen (D6 go). Library tab lists "All Artists" first (id 0 = every album) so the full-album scroll can be judged. Queue tab is a placeholder (D1 default).
- Scan test: `rescan done` fires even with no change; `lastscan` unchanged after a no-change scan; scans take about 7 minutes and reconnect players.

