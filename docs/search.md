# cLiMS – Search reactivity: server API vs local index

Research only. All numbers measured on your server (LMS 9.1.1, Raspberry Pi, 19 948 tracks / 2 008 albums / 1 004 artists), from the Windows PC over LAN. HTTP JSON-RPC, 5 runs each unless noted; times include network.

## 1. Target
Typing in the header search bar switches to the Search tab and shows results grouped as Artists / Albums / Tracks (your reference screenshot), updating while typing. Rule of thumb: under about 100 ms feels instant, under about 250 ms feels responsive, over about 400 ms feels laggy.

## 2. What the LMS API gives us

| Query | Avg latency |
|---|---|
| Global `search` (artists + albums + tracks in one call, 10 each) | `i` 251 ms · `ir` 123 · `iro` 104 · `iron` 98 · `iron m` 161 · `maiden` 135 · **`the` 444 ms** |
| `artists search:iron` | 24 ms |
| `albums search:iron` | 47 ms |
| `titles search:iron` | 119 ms |
| Three typed queries in parallel | about 300 ms incl. test-harness startup (LMS runs requests mostly one at a time, so parallel gains are small) |
| Reply size | under 1 KB for 10 results per group |

Semantics ✔:
- **Word-prefix match, not substring.** `maid` finds Maiden; `aiden` finds nothing. Multiple words are ANDed in any order (`iron mai` = `mai iron`).
- Diacritics are folded: `andres` and `andrés` both match.
- The global `search` reply uses `contributors_loop` (not `artists_loop`) plus `albums_loop`, `tracks_loop` and the `*_count` totals.
- No typo tolerance, no ranking control, no "popularity" ordering.

Verdict on the API alone:
- A **typical specific word** (3+ characters, e.g. `iron`) answers in about 100 ms: acceptable.
- **Short or very common prefixes** (`i`, `the`) take 250–450 ms because many tracks match.
- Every keystroke is a request, so debouncing (about 120 ms) and cancelling stale requests are mandatory; otherwise the Pi queues requests and results arrive out of order.
- It never gets truly instant, and every keystroke loads the Pi (which also streams your music).

## 3. Local index: what a sync would cost ✔

Full-library download, paged (`titles` limited to 2 000 per call; a single call of 25 000 failed, 5 000 worked):

| Data | Size | Time |
|---|---|---|
| Artists (1 004) | 111 KB | 1.4 s |
| Albums (2 008) | 461–630 KB (depends on tags) | 6–8 s |
| Tracks (19 948), tags `alts` (artist, album, number, artist_id) | about 2.5 MB | about 56 s |
| Tracks, tags `acdegijlpqrstTuy` (rich) | 7.9 MB | about 133 s |
| Tracks, no tags | 83 KB per 2 000 | about 3 s per 2 000, about 30 s total |

So an initial build is about 1 minute with a light tag set, in the background, once. The index in memory would be roughly 10–30 MB.

Change detection:
- `info total songs ?`, `rescanprogress`, `serverstatus` → `lastscan` (timestamp) ✔ all available and cheap.
- Notifications over the CLI socket: `rescan done`, `library changed`.
- There is **no "changed since" filter** for tracks. `albums sort:changed` exists, but I have not tested it for incremental sync.
- Practical rule: when `lastscan` or the song count changes, rebuild in the background and swap the index. The scan is rare for a music collection that changes occasionally.
- IDs can change on a full rescan, so key the cache by `lastscan` and re-resolve queue entries by file `url` (see `jsonrpc-client.md` section 5).

## 4. Comparison

| | API only | Local index (in memory, optionally persisted in SQLite) |
|---|---|---|
| Latency per keystroke | 100–450 ms, load on Pi | **under 5 ms** for 20 k tracks, no network |
| Search quality | word-prefix only, no ranking | substring, fuzzy/typo-tolerant, own ranking (exact > prefix > contains, artist boost), diacritic folding, "type-ahead" highlighting |
| Result grouping and counts | server gives 3 groups | any grouping, e.g. "Tracks (10 / 29)" with "More" |
| Pi load | one request per keystroke | none while typing |
| Complexity | low | sync, staleness handling, invalidation, startup time |
| Staleness | none | seconds to minutes after a rescan, unless using notifications |
| Offline browsing | no | yes (browse only; playback needs the server) |
| Benefit beyond search | none | instant Library tab navigation, A–Z jumps, counts, filters by genre/year without round trips |

## 5. Recommendation [inferred]
**Hybrid, server first, local index as the main path:**
1. Start-up: show the UI immediately; load a persisted index from disk (SQLite file, keyed by `lastscan`) if present; compare `lastscan` and `info total songs`.
2. If missing or stale: build in the background (artists, then albums, then tracks with a light tag set, paged 1 000–2 000, a few seconds per page). While it builds, **search falls back to the server `search` command** with debounce and cancellation.
3. When the index is ready, search locally (in-memory structures, e.g. normalised-string prefix/contains scan over about 20 k rows; no extra engine needed at this size). Result screen: top 2 artists, 4 albums, 10 tracks, with "More".
4. Keep it fresh: listen for `rescan done` / `library changed` on the CLI socket (port 9090) and rebuild.
5. The queue and the waveform cache still use ids/urls from the live server.

Why not API only: your own target ("almost instant") fails for short prefixes and common words (250–450 ms), and each keystroke loads a Pi that also serves audio.
Why not local only: the first run needs the server anyway, and changes need syncing; the fallback keeps both working.

## 6. Decisions (from you)
1. **Persistence: SQLite.** The index is saved to disk and reloaded at launch; a rebuild happens only when `lastscan` or the song count changes.
2. **Search scope: artists, albums, tracks and genres. Not composers.** Genres are cheap to sync (`genres` query; track genre via the `g`/`p` tags). Composers, comments and other fields stay out of the index, which also keeps the track sync light.
3. **Thumbnails: fetched lazily**, only when a result row becomes visible (not prefetched). Source: `cover_48x48_o.png` or similar (about 6 KB, about 40 ms each).

## 7. Implications of those decisions [inferred, not tested]
- **SQLite as storage, memory as the search engine.** 20 k tracks load into memory in well under a second, so searching can stay a plain in-memory scan (normalised strings, prefix and contains, own ranking). SQLite then only has to persist and reload. FTS5 is optional: `unicode61 remove_diacritics` handles accents, but FTS5 matches tokens and prefixes, not arbitrary substrings (the `trigram` tokenizer does substrings, in SQLite 3.34 or later). Start without FTS5 and add it only if the scan proves too slow.
- **Package:** `Microsoft.Data.Sqlite` (bundled native SQLite). No ORM needed for 4 tables (`artists`, `albums`, `tracks`, `genres`) plus a `meta` table (`lastscan`, `songcount`, `schema_version`).
- **Sync order and cost:** artists (1.4 s), genres (tiny), albums (6–8 s), then tracks with a light tag set (about 1 min). Search works as soon as artists and albums are loaded (artists/albums/genres groups), with tracks falling back to the server until their sync completes.
- **Cache location:** per-user app data folder, one file per server (keyed by server uuid `7c1f32ca-…` from `serverstatus`), safe to delete.
- **Thumbnails, lazy:** an in-memory LRU of decoded half-block cell arrays (for example a few hundred entries), fetched with a small concurrency limit (2–3 requests) and cancelled when the row scrolls out of view. Whether to also keep them on disk is a separate choice (not decided); the server answers in about 40 ms, so it may not be needed.
- **Artist rows** use an icon, since LMS has no artist portraits.

## 8. Decisions, round 2
- Thumbnails: **memory only** (no disk cache).
- The index also feeds the **Library tab** (artist list, album lists and counts), not only search.
- The header shows the **sync state** next to the player name (section 10).

## 9. Incremental sync: deeper research ✔

Tested on your server. The only write was one `rescan album 853235` (Immortal, *Diabolical Fullmoon Mysticism*), run twice; it re-read 7 tracks and changed only their `lastUpdated` stamps. The album and track ids, counts, tags and files are unchanged.

| Signal | What I found |
|---|---|
| Global `lastscan` (`serverstatus`) | 05:29:16. **Did not change** after `rescan album`. Likely moves only on full or "new & changed" scans (not tested: I did not start one). |
| `info total songs/albums` | Cheap, but only catches added or removed items, not edited tags. |
| `rescan done` / `library changed` events (CLI) | **Not sent** for `rescan album` (only the echoed `rescan album <id>` line arrived). They probably fire for full scans (untested). |
| `albums sort:changed` | **Capped at 100 albums** (`count:100`, regardless of the page size). It did **not** reorder after the targeted rescan, so it is not a reliable change log. |
| Per-track `U` (lastUpdated), `D` (addedTime), `n` (modificationTime) | Available on `titles`. `lastUpdated` **did change** after the rescan (05:11:16 to 13:07:3x for the 7 tracks); `addedTime` stays; `modificationTime` is the file's own mtime. This is the only reliable per-item change marker. |
| Server-side filter by time | None. A `since:` parameter is ignored (still returned all 19 948). |
| Page limits | `albums`, `artists`, `genres`: one call of 5 000 returned everything. `titles`: 5 000 per call worked; 25 000 failed. |
| Cost of a `lastUpdated`-only pass over all tracks (`titles tags:U`) | about 3 s per 2 000 tracks, so about 30 s total, not much cheaper than the full light sync (about 56 s), though far smaller (about 83 KB per 2 000 plus the stamp). |
| Cost of a targeted `rescan album` on the server | First run blocked the server for over 30 s (one of my polls timed out); the second took 4 s. LMS is busy while scanning, so avoid hammering it during a scan. |

Conclusions [inferred]:
1. **No cheap global "what changed" log exists** for edits made outside a full scan. A targeted rescan (what plugins and some tools do) changes only `lastUpdated`.
2. **Trigger a full background resync** when the cheap signals move: `lastscan`, song/album/artist/genre counts, or a `rescan done` / `library changed` event. This covers added/removed music and normal "look for new and changed music" scans.
3. **Self-healing on view**: whenever the Library tab shows an album (or a queue row is shown), re-fetch that album's tracks live (about 100 ms, tags `alts…`) and update the index row. Edits made by targeted rescans then show up exactly where you look, with no global resync.
4. **Manual "Resync now"** command in the UI for anything else.
5. Optional later optimisation: a `lastUpdated`-only background pass (about 30 s) to find changed albums and refresh only those. It is a bandwidth saving, not a time saving, so I would not build it first.

## 10. Sync status in the header
Shown next to the player name (top right of the header):

| State | Shown as | When |
|---|---|---|
| Not synced | `○ Not synced` | No index on disk and sync not started, or the user cancelled it. |
| Syncing | `◐ Sync in progress, live search only` plus progress (for example `artists ✓ albums ✓ tracks 42%`) | Initial build or resync; search uses the server for groups not yet loaded. |
| Synced | `● Synced` plus age (`2 h ago`) | Index matches `lastscan` and counts. |
| Out of date | `◔ Server changed, resync pending` | A signal moved; resync starts or waits for you. |
| Server unreachable | `✕ Offline (using local index)` | Search and browsing still work from the saved index; playback does not. |

Details:
- Search keeps working in every state: local when synced (or when that group is loaded), otherwise the server.
- Click or shortcut on the status opens "Resync now" and "Cancel".
- The status is driven by the sync service's state (a small state machine in the library), which is independent of the UI.

## 11. Decisions, round 3
1. **Full-scan trigger test: deferred.** Whether a full or "new & changed" scan updates `lastscan` and sends `rescan done` / `library changed` stays unconfirmed; the design treats those as probable triggers and falls back to count changes and the manual "Resync now".
2. **Resync is never automatic: ask first.** When a trigger fires, the status becomes `◔ Server changed, resync pending` and the user is prompted (Resync now / Later). The very first build (no index on disk) also asks once before starting. Until the user agrees, the old index keeps serving (marked possibly stale), and on-view album refresh (section 9, point 3) still runs.
3. **Mixed sources while syncing:** groups not yet loaded come from the server `search`, loaded groups from the local index (for example local artists and albums plus server tracks), in the same result screen.

## 12. Decisions, round 4
- "Later" on the resync prompt: **ask again on the next launch** (no snooze within a session, but the status stays `◔ Server changed, resync pending`).

## 13. Full "new & changed" scan test ✔ (supersedes decision 1 of section 11)

I started `rescan` (look for new and changed music) with the CLI socket open. Nothing had been added or edited on disk, so this tested the signalling and cost, not a real change.

| Observation | Result |
|---|---|
| Duration | **436 s (about 7 min 16 s)** on the Pi. Discovery of `/mnt/Music` finished within about 60 s; most of the time went to the full-text search plugin indexing (`plugin_fulltext`) and post-scan steps. `rescanprogress` reports `rescan:1`, a `steps` list and a `fullname` such as `Discovering files/directories: /mnt/Music`, with per-step percentages. |
| `rescan done` event | **Sent twice** (at 407 s and 436 s). So it fires at the end of a scan, even when nothing changed. Treat it as "a scan finished, go check", not "the library changed". |
| `library changed` event | **Not sent** (nothing changed). Not confirmed for real changes. |
| `lastscan` (`serverstatus`) | **Unchanged** (2026-11-06 05:29:16). So it does not move on a no-change scan; whether it moves when files change is still untested. |
| Counts (songs/albums/artists/genres) | Unchanged (19 948 / 2 008 / 1 004 / 195). |
| Track `lastUpdated` | Unchanged for the two tracks sampled. |
| Side effects | At the end of the scan LMS **disconnected and reconnected the player** (`client disconnect` then `client reconnect`, plus `prefset server positionAtDisconnect 190.333`). The Pi was playing; it came back connected at the same position (190.3 s). Another LMS client (192.168.1.x (another client)) was also re-registered. Other noise on the socket: `material-skin …` messages (the Material skin plugin is installed), `menustatus ARRAY(…)`, `prefset … HASH(…)`. |

Design consequences:
1. **Trigger = `rescan done`, then compare**: on that event (and at launch) read `lastscan` and the counts; propose a resync only if either changed. A `rescan done` with no change must not nag the user.
2. **A real new-file scan is still untested**, so whether `lastscan` moves and whether `library changed` fires remain unconfirmed. Counts plus on-view album refresh remain the safety net.
3. **Handle `client disconnect` / `client reconnect`** for the player in the event stream: the status/now-playing UI should show "player reconnecting" and re-query `status`.
4. **Ignore chatter**: the event parser must tolerate and drop `material-skin`, `menustatus`, `prefset`, `artworkspec` and similar lines, and not treat `ARRAY(0x…)` / `HASH(0x…)` payloads as data.
5. **Do not start scans from the client** (expensive and disruptive: 7 min, a player disconnect), except via an explicit user action.
6. **Initial sync can use the idle server, but not during a scan**: the server answers slowly while scanning (some of my status polls timed out); the sync service should pause or back off when `rescanprogress` reports `rescan:1`.

## 14. Still open
1. Whether adding or editing a real file moves `lastscan` and fires `library changed` (would need a real change, for example touching a tag in a test file).

## 15. Implemented (Step 3)
The index, sync and status described above now exist (`src/Clims.Catalog`, README there). How it came out:

| Decision / measurement | Result |
|---|---|
| Persistence | SQLite via `Microsoft.Data.Sqlite`, one file per server UUID in `%LOCALAPPDATA%\cLiMS`, whole index saved in one transaction, schema version 1. Memory is the read model; SQLite only persists. |
| Tracks synced | Light set: artist, artist id, album id, track number, disc, duration, genre (no file URL: that arrives with an on-view refresh). About 380 KB per 2 000 tracks. |
| Cost on the Pi | genres + artists + albums about 10 s; tracks 6.8 s per 2 000 with `sort:albumtrack`, so a full sync takes **about 1.5 minutes** (two real syncs in the integration tests: 2 min 58 s for both). |
| First build | Artists and albums are published after about 10 s, so the Library tab is usable while tracks stream in; visible albums fetch their own tracks (on-view refresh). Nothing partial is written to disk. |
| Triggers | `lastscan` or any of the four counts differing from the index. A finished scan with no difference is silent. Checked at startup and on `rescan done`, `library changed`, and event-stream reconnects. |
| Prompting | Never automatic. First build and each new change ask once (Y / Later); Later asks again at next launch. F5 or a click on the status offers resync or cancel at any time. |
| Cancel / failure | The old index and the file stay as they were; status returns to `Server changed, resync pending` (or `Not synced` on a first build), with the reason shown as a notice. |
| Scanning | The sync waits (polls `rescanprogress` every 5 s, before each tracks page). |
| Search | Not part of Step 3 beyond a simple in-memory word-prefix scan so the Search tab works on real data (about 20 000 tracks per keystroke without any index structure); ranking, genres and the mixed server results are Step 4. |

Surprises: the server's counts had moved since section 3 (19 960 tracks, 2 009 albums), so change detection fired on the first run; `artists` includes track-only people; and the genre tag needs an explicit sort (see `jsonrpc-client.md`).

## 16. Implemented (Step 4)
Ranked search over the local index, with the server filling gaps (`src/Clims.Catalog/Search`, `src/Clims.Tui/Ui/SearchTab.cs`, `ResultsView.cs`).

| Topic | Result |
|---|---|
| Matching | Every query word must occur in the (accent- and case-folded, punctuation-normalised) name, in any order, **anywhere in a word**. The server only matches word starts, so `ure` finds The Cure locally and not on the server. Albums match on title + artist, tracks on title + artist + album. Genres are searched too; composers are not. |
| Ranking | Per item: exact 100, starts with the query 80, every word starts a word 60, words only inside words 30. An album or track that matches only through its artist (or album) scores half of its tier; a title match whose artist also starts with the query gets +5 (the artist boost). Ties: shorter name, then alphabetical. Groups are always listed Artists, Albums, Tracks, Genres. |
| Counts | Each group keeps its best 50 / 100 / 200 / 30 hits and the total number of matches, so the headers read `Tracks (10 / 220)`; "More…" shows the rest of the kept hits. Default view: 2 artists, 4 albums, 10 tracks, 3 genres. |
| Speed | On the real saved index (19 960 tracks, 2 009 albums): `iron` 1.8 ms, `the` 7 ms, single letters 13-15 ms (they match almost everything), including ranking. No extra index structure: a scan over precomputed normalised strings. Measured with the opt-in test `Local_search_on_the_real_index_is_well_under_fifty_milliseconds` (`CLIMS_CATALOG_DB=<path to catalog-*.db>`, works on a copy). |
| Keystrokes | The local answer is computed synchronously on every keystroke, so the old 250 ms debounce is gone. |
| Mixed sources | `SearchService`: no index at all, every group comes from the server (`artists`, `albums`, `titles` with `search:`, and the global `search` for genres); index without tracks yet (first sync running), only Tracks come from the server. The server request waits 120 ms after the last keystroke, cancels the previous one, and an answer for older text is dropped (sequence number). Server hits are ranked with the same rules. While the answer is pending the screen says `Searching the server for …`. |
| Offline | An unreachable server keeps the local groups and adds a note; a complete index never asks the server. |
| Highlights | The matched fragments are drawn in the warning colour, on both lines of a row (`TextFolding.Highlights` maps folded matches back to the original text, so accents do not shift anything). |
| Thumbnails | Albums and tracks show a 4x2-cell cover (Sixel when available) from the Library's cover cache; artists show an icon block. Lazy and cancelled when scrolled away. |

Findings: LMS returns album titles only when the album tag is requested (found by the first fallback screenshot); server tracks carry their own album title and artwork id because the index may not know them yet; a track hit whose album is not in the index cannot be played (notice) until a sync.
