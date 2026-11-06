# cLiMS – Research notes

Goal: a .NET 10 TUI client for Lyrion Music Server (LMS) – browse library, play tracks/albums, manage custom queues.
Target server: LMS **9.1.1** at `http://192.168.1.20:9000/` (piCorePlayer, one player: Squeezelite `b8:27:eb:a9:77:8f`).

Legend: **[verified]** = tested against the live server; **[sourced]** = from web sources, to be confirmed; **[inferred]** = my reasoning.

---

## 1. LMS interfaces

| Interface | Port | Status | Use in cLiMS |
|---|---|---|---|
| JSON-RPC over HTTP `POST /jsonrpc.js` | 9000 | [verified] works, no auth | Primary request/response API |
| CLI (line protocol) | 9090 | [verified] port open | Push events via `listen` / `subscribe` |
| HTTP content (cover art, downloads, streams) | 9000 | [sourced] | Artwork, raw audio for waveform |
| Slim protocol | 3483 | [verified] open | Player<->server only; not used |

Request shape:
```json
{"id":1,"method":"slim.request","params":["<playerMAC or ''>", ["<command>", arg1, "tag:value"]]}
```
Verified example: `["", ["serverstatus",0,5]]` returned version 9.1.1 and the player list.

### Commands relevant to the client (names [sourced]/known; exact params to confirm in the server's own CLI docs at `/html/docs/help.html` or [lyrion.org CLI reference](https://lyrion.org/reference/cli/introduction/))
- Browse: `artists`, `albums`, `titles`, `genres`, `search`, `years`, `playlists`, `radios`, `favorites items` — paged with `<start> <count>`, enriched via `tags:` (e.g. `tags:aAlKcdtyj`).
- Playback: `play`, `pause`, `stop`, `button`, `time <secs>`, `mixer volume`, `playlist index`, `playlist repeat/shuffle`.
- Queue (current player playlist): `playlist play|load|add|insert|delete|move|clear|loadtracks|addtracks|inserttracks <track_id:/album_id:/artist_id:…>`, `status` (queue contents + `playlist_timestamp` for change detection) — [sourced: [playlist reference](https://lyrion.org/reference/cli/playlist)].
- Saved playlists: `playlists`, `playlist save <name>`, `playlist resume`, `playlistcontrol cmd:load|add|insert playlist_id:N` — [sourced, old bug thread; confirm].
- Elapsed time: `time ?` (decimal seconds) or `time` field in `status`.

### Live updates
`listen 1` on port 9090 streams notifications (playlist changes, newsong, pause, volume, etc.). The alternative is polling `status` every ~1 s and comparing `playlist_timestamp`. Plan: JSON-RPC for requests + one CLI socket for events, with polling as fallback.

---

## 2. Custom queues — design options
LMS has **one current playlist per player**, plus *saved playlists*. "Custom queues" therefore need a choice:

| Option | How | Pros | Cons |
|---|---|---|---|
| A. Client-side queues | Store named queues locally (JSON/SQLite); "activate" = `playlist clear` + `playlist addtracks` | Unlimited, instant, no server pollution | Not visible in other LMS clients |
| B. LMS saved playlists | `playlist save`, `playlistcontrol` | Shared with all clients | Heavy naming/ID management, edits are awkward |
| C. Hybrid (recommended) | Local queues as the source of truth, with export/import to saved playlists | Best of both | More code |

[inferred] Recommendation: **C**, starting with A.

---

## 3. TUI framework choice (goal: best graphical options)

Findings are from web searches; version numbers are unverified and should be checked on NuGet before deciding.

| Framework | Model | Graphics / visuals | Notes |
|---|---|---|---|
| **[Terminal.Gui v2](https://gui-cs.github.io/Terminal.Gui)** | Full widget toolkit (windows, focus, menus, tables, tree, lists) | 24-bit true color (with 16-color fallback); Sixel raster output + `ImageView` (listed in 2.4.4 notes); `GraphView` (charts) | Most complete interactive toolkit. v2 sources conflict on stable vs. beta (2.4.x seen) — verify. [sourced](https://www.nuget.org/packages/Terminal.Gui/2.0.0-develop.5259) |
| **[Spectre.Console](https://github.com/spectreconsole/spectre.console)** | Output renderer (tables, charts, canvas, live display) | Rich markup, BarChart, Canvas, `Live` | Not a retained-mode interactive toolkit; weak for focus/input-heavy apps. [sourced](https://dev.to/littlelittlecloud/net-tui-frameworks-compared-terminalgui-spectreconsole-and-razorconsole-56ge) |
| **SharpConsoleUI** | Windowed toolkit built on Spectre | Any Spectre `IRenderable` embeddable as a control | Layout less expressive than Terminal.Gui per its own comparison. [sourced](https://dev.to/nikolaos_protopapas_d3bd6/building-terminal-uis-in-net-how-sharpconsoleui-complements-terminalgui-hb9) |
| **[Consolonia](https://terminalunlocked.com/tools/consolonia/)** | Avalonia-on-terminal: XAML, data binding, templating, themes | Colors downsampled to ANSI/256; no pixel-oriented drawing; images not mentioned | Best fit if you like MVVM/XAML; weakest "graphics". [sourced](https://medium.com/@tomlairdmcconnell/consolonia-avalonia-for-console-applications-4eb13b3d487b) |
| RazorConsole, XenoAtom.Terminal.UI, Hex1b | — | **Not researched** (searches found nothing) | Newer entrants; evaluate if desired |

### Terminal capabilities matter more than the framework
- **Windows Terminal** supports **Sixel** since 1.22; it does **not** support the Kitty graphics protocol [sourced](https://terminaltrove.com/compare/terminals/kitty-vs-windows-terminal/). So cover art in WT = Sixel; Kitty/WezTerm/Ghostty/iTerm2 have other image protocols.
- Without image protocols, fallbacks are half-block/braille characters with true color (works everywhere, lower fidelity).

### Recommendation [inferred]
**Terminal.Gui v2** — the only candidate with both a full interactive widget set (needed for library tree, queue lists, focus, keybindings) *and* native true color + Sixel/ImageView + GraphView. Spectre.Console renderables can still be embedded (via interop/SharpConsoleUI) if a specific chart is needed.
Risk to check first: current Terminal.Gui v2 stability on .NET 10 and Sixel behaviour in Windows Terminal. A 1-day spike (library list + cover art + a live-updating bar) would settle it.

---

## 4. Waveform / peaks during playback

**UPDATED – Short answer: LMS itself exposes nothing, but a genuinely live visualizer IS feasible via Squeezelite's own visualiser feature (`-v`) (section 4b). A static waveform with a playhead remains the robust fallback / complement (section 4).**

Facts:
- Audio is decoded by **Squeezelite on the Pi**; LMS only sends the compressed/transcoded stream. LMS exposes no peak/waveform/spectrum endpoint over JSON-RPC or CLI [inferred – none found in docs or search; confirm in the CLI reference].
- Squeezelite visualizers (where supported) draw on local hardware displays; they are not sent over the network [inferred].

Feasible approaches:

| Approach | How | Quality |
|---|---|---|
| **1. Precomputed waveform (recommended)** | Download the track (`/music/<id>/download`), decode in .NET (e.g. NAudio/FFmpeg/ffprobe pipe), compute min/max peaks per N samples, cache by track id. Draw it in the TUI (braille/block chars or Sixel image) and move a playhead using `time ?`/`status` elapsed (poll ~4–10 Hz, interpolate locally between polls). | Exact overview waveform; playhead sync within ~100–300 ms |
| 2. Pseudo-live meter | Tap `/stream.mp3` for the player and analyze it live | Separate stream, buffered/delayed and not locked to what the speakers play; transcoded to MP3; adds server load. Poor sync — not recommended |
| 3. Live spectrum on the Pi | Custom code on piCorePlayer (ALSA loopback / tap) pushing levels to the client | Truly live, but outside LMS; high effort |

Notes for approach 1:
- Cache peaks (e.g. 1–2 k points/track) in SQLite or sidecar files to avoid re-decoding.
- Pre-fetch the next queue item's waveform.
- Elapsed time from LMS includes player buffering offsets; expect a small constant offset you can calibrate.
- Lossless/large files: decode in a background task; show a spinner until ready.

---

## 4b. Live waveform – investigation result

Your player (verified): `squeezelite`, firmware `v2.0.0-1476-pCP` (ralph-irving build), plays FLAC from `/mnt/Music`.

### Option L1 – Squeezelite visualiser (`-v`) + UDP relay  ✅ feasible, best candidate
- Squeezelite `-v` "creates a shared memory segment that contains some of the audio being played, so that an external visualiser can read and process this" ([man page](https://ralph-irving.github.io/squeezelite.html)). Used today by Jivelite, CAVA, etc. [sourced]
- SHM `/dev/shm/squeezelite-<MAC>` holds `vis_t { rwlock; buf_size; buf_index; running; rate; updated; int16_t buffer[16384] }` – a ring buffer of the **actual PCM being sent to the DAC** (layout taken from vismux's copy of the struct; not verified against Squeezelite source; interleaving not stated) [sourced].
- The SHM is local to the Pi, so it must be forwarded. **[vismux](https://github.com/blaisedias/vismux)** (C, GPLv3+, prebuilt for armhf/aarch64/armv6, documented on piCorePlayer) does exactly this over **UDP**: source on the Pi (`vismux --player --wait-for-shm`, started via pCP Tweaks → User command), 60 fps polling, delta packets of new int16 samples, default port 23483 (discovery 23484, magic `VISMUXv0`). Wire format: 10-byte header + 16-byte vis header (`buf_size, buf_index, running, rate`, big-endian) + raw little-endian int16 samples. [sourced; README was read by an agent, not by me]
- A .NET client can **speak the vismux destination protocol directly** (subscribe with `PACKET_REQ`, keepalive `PACKET_ACK`, receive deltas) – no SHM needed on Windows (vismux itself is POSIX-only). Alternative: write our own tiny relay on the Pi.
- Requirements/changes on the Pi: add `-v` to Squeezelite options (pCP Squeezelite page), install vismux, open UDP 23483/23484. Client side: compute waveform/peaks/spectrum (FFT) from received samples.
- Sync: samples are what Squeezelite is outputting, so the picture is as close to the speakers as it gets, plus network + ≤16.7 ms poll. No timestamps in the protocol; expect tens of ms; **needs measuring**.
- Gapless: Squeezelite just keeps writing the continuous output stream, so the visualiser sees track changes seamlessly (`running` flag only drops on pause/stop) [inferred]. Sample-rate changes surface via `rate` field.
- Risks: UDP loss (no retransmit; full sync ≈ 32 KB, IP-fragmented, recovers on next full sync); `-v` unverified on this exact pCP build (check `ls /dev/shm/squeezelite-*` after enabling); small CPU cost on the Pi; project is young (copyright 2026, version 0.0.9) and the Lyrion forum thread could not be read (403), so field bugs are unknown.

### Option L2 – Second (synced) Squeezelite on the PC piped to the client
- Run `squeezelite -o - -a 16` on the PC as another player, add it to a **sync group** with the Pi; raw PCM comes out of stdout for analysis ([man page](https://ralph-irving.github.io/squeezelite.html), [forum example](https://forums.lyrion.org/forum/user-forums/3rd-party-software/109522-pipe-squeezelite-to-icecast/page2)). [sourced]
- Pros: no change on the Pi, works with stock software. Cons: sync groups with software players are only loosely accurate ([wiki](https://wiki.lyrion.org/index.php/Synchronization)); an extra player appears in LMS; sync changes player behaviour (power/volume/queue shared); stdout needs a non-ALSA build/ may be buggy in some versions ([issue #62](https://github.com/ralph-irving/squeezelite/issues/62)); the stdout reader must consume continuously or playback stalls. Not recommended unless L1 fails.

### Option L3 – tap `/stream.mp3` (verified endpoint returns 200)
- It is a separate transcoded stream, not locked to the speakers; useless for sync. Rejected.

### Option L4 – LMS-side
- No CLI/JSON-RPC command for peaks/spectrum found. Rejected.

### Decision
1. **Primary: static waveform with playhead (section 4)** – works with zero Pi changes, is exact for the whole track and shows the "future" too.
2. **Optional live layer: L1 (vismux)** for VU/spectrum/scope in the Now-Playing pane. Spike: enable `-v`, run vismux, log packets from a .NET UDP client, measure latency against the `status` elapsed time.
3. Keep the visual layer behind an interface (`IVisualSource`) so either can be swapped/combined.

---

## 4c. Gapless playback – requirements

**Gapless is done by LMS + Squeezelite, not by the client. The client's only duty is to never break it.** (Squeezelite supports gapless natively – [Wikipedia](https://en.wikipedia.org/wiki/Squeezelite) / [lyrion.org](https://lyrion.org/players-and-controllers/squeezelite).)

Current server/player settings (verified via JSON-RPC):
| Pref | Value | Meaning |
|---|---|---|
| `transitionType` (player) | `0` | no crossfade → gapless-compatible |
| `transitionSmart` (player) | `1` | smart transition on |
| `replayGainMode` | `0` | off (avoids gain jumps between tracks) |
| Files | FLAC | gapless-friendly (no MP3 encoder-delay problem) |
| Stream | LMS pre-buffers next track and Squeezelite chains it | automatic |

Rules for the client:
1. **Never stop/restart to advance.** Let the player advance on its own. Don't `playlist clear` + reload while playing.
2. **Modify the live queue in place** with `playlist add / insert / delete / move` (+ `playlist index` to jump). Reloading the whole queue (`playlist loadtracks`/`play`) restarts playback = audible gap. (Semantics of add/insert vs. the pre-buffered "next track" to confirm — inserting right after the current track may reset the pre-buffered next stream.)
3. **Switching custom queues** is inherently a gap unless done at a track boundary: strategy = keep current track playing, delete everything after it, append new queue's tracks ("play after current"); only hard-switch on explicit user request.
4. Don't change `transitionType` (crossfade), `replayGainMode` or sync membership at runtime.
5. Gapless across **sample-rate/bit-depth changes** depends on the DAC/ALSA setup and may still produce a click/gap even though LMS sends the next track (to be tested on your Pi).
6. Any waveform/visualiser work must be read-only: static analysis downloads the file via HTTP (no player impact); the live path (L1) only reads the SHM.
7. Test plan: album with continuous tracks (live/classical), check gap with `-v` PCM capture: zero-run length in samples at the track boundary should be ≈0.

---

## 5. Proposed architecture (sketch)
```
cLiMS.Core     LmsClient (JSON-RPC), LmsEvents (CLI 9090 listen), models, QueueStore (local)
cLiMS.Audio    PeakExtractor + cache
cLiMS.Tui      Terminal.Gui views: Library tree | Album/track list | Queue | NowPlaying (art + waveform)
```

## 6. Open questions / next steps
1. Confirm exact command params (`playlist addtracks`, `playlistcontrol`, `status` tags) against the server's `/html/docs/help.html`.
2. Verify Terminal.Gui v2 latest version, .NET 10 compatibility, and Sixel in your terminal.
3. Decide queue strategy (A/B/C above).
4. Decide which terminal(s) must be supported (Windows Terminal vs. others) — drives the image strategy.
5. Check installed LMS plugins (Material skin, etc.) in Settings → Plugins.

## Sources
- [Lyrion CLI introduction](https://lyrion.org/reference/cli/introduction/), [Playlist CLI reference](https://lyrion.org/reference/cli/playlist)
- [.NET TUI Frameworks Compared (DEV)](https://dev.to/littlelittlecloud/net-tui-frameworks-compared-terminalgui-spectreconsole-and-razorconsole-56ge)
- [SharpConsoleUI vs Terminal.Gui (DEV)](https://dev.to/nikolaos_protopapas_d3bd6/building-terminal-uis-in-net-how-sharpconsoleui-complements-terminalgui-hb9)
- [Terminal.Gui on NuGet](https://www.nuget.org/packages/Terminal.Gui/2.0.0-develop.5259), [Terminal.Gui v2 docs mirror](https://gitea.avinfosrv.duckdns.org/Git_Mirror/Terminal.Gui/raw/commit/2be83972036020ce26794a63ad677abf1b54b65f/docfx/docs/newinv2.md)
- [Consolonia](https://terminalunlocked.com/tools/consolonia/), [Consolonia article](https://medium.com/@tomlairdmcconnell/consolonia-avalonia-for-console-applications-4eb13b3d487b)
- [Kitty vs Windows Terminal](https://terminaltrove.com/compare/terminals/kitty-vs-windows-terminal/)
