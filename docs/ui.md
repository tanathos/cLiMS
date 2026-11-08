# cLiMS – UI research and layout proposal

Research only. Reference: the MusicBee-style "Album Artist" screenshot you provided. Terminal framework assumption: Terminal.Gui v2 (see `research.md` section 3).

## 1. Layout

Always full screen (alternate screen buffer, resize-aware). Three fixed regions:

```
┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│                                                                                              │ HEADER (3 rows:
│   cLiMS /  [ search…                         ]        piCorePlayer ● Synced (2 h ago)  🔊 70 │ padding, content,
│                                                                                              │ padding)
├──────────────────────────────────────────────────────────────────────────────────────────────┤
│ [ Search ] [ Library ] [ Queue? ]                                                            │ TAB STRIP
├───────────────────────┬──────────────────────────────────────────────────────────────────────┤
│ Album Artist ▾        │ Agalloch                                                             │ CENTRAL
│  All Artists          │                                                                      │ (tabbed)
│  [Unknown]            │                                                                      │
│  …                    │ ┌────────┐ The Serpent & the Sphere                          60 min  │
│▶ Agalloch             │ │ cover  │ 2014 · Black Metal; Folk Metal                            │
│  Aghora               │ │ 20x10  │  1 Birth and Death…   10:28    6 Cor serpentis      2:58 │
│  …                    │ └────────┘  2 (Serpens caput)     3:06    7 Vales Beyond…      6:48 │
├───────────────────────┴──────────────────────────────────────────────────────────────────────┤
│ 02:31 ━━━━━━━━━━━━━━━●──────────────────────────────────────────────────────────── 07:12     │ FOOTER
│ ┌──────┐ ▁▂▅▇█▆▃▂▁▂▄▆█▇▅▃▂▁▁▂▃▅▇█▆▄▂▁▁▂▃▅▆▇█▆▄▂▁   ⏮  ⏪10  ▶/⏸  10⏩  ⏭      🔊 ▁▂▃▄▅ 70%  │ (4–6 rows)
│ │ cover│ Artist – Title · Album                                                              │
│ └──────┘                                                                                     │
└──────────────────────────────────────────────────────────────────────────────────────────────┘
```

- **Header (always visible, implemented as 3 rows):** one blank padding row above and below the content row, two columns of padding left and right, on a slightly darker background than the body. Content: search bar (global shortcut `/`), plus a small status area: player name, **library sync status** (`Not synced`, `Sync in progress, live search only`, `Synced`; also `Server changed, resync pending` and `Offline (using local index)`, details in `search.md` section 10) and connection state. Typing debounces about 250 ms and runs the LMS `search` command; results go to the Search tab, which the header switches to.
- **Footer (always visible):**
  - Top row: global progress bar with elapsed / total time. Click to seek (`time <secs>`).
  - Left: now-playing cover and waveform/peaks (see `research.md` section 4; static waveform plus playhead first, live `-v` layer optional).
  - Right/center: Prev, -10 s, Play/Pause, +10 s, Next and volume. Maps to `playlist index -1`, `time -10`, `pause`, `time +10`, `playlist index +1`, `mixer volume`.
- **Central view:** tab control.
  - **Search** tab: results grouped by Artists / Albums / Tracks (details to define).
  - **Library** tab: left filter pane plus right results pane, as in MusicBee.
  - **Queue** tab (not in your list; see open questions): needed to manage custom queues.

## 2. Library tab (MusicBee-style)
- **Left pane:** one-column list with a switchable dimension (header drop-down: Album Artist / Artist / Genre / Year / Folder…), "All …" on top, A–Z jump using the `textkey` from LMS. About 1 004 artists, so virtualised list and type-ahead find.
- **Right pane:** the selected artist's name as a heading, then a scrollable column of album blocks. Each block has cover (left), title, year · genres, total duration, and the tracklist in **two columns** (as in the screenshot) with track number, title and duration. No "Album and Tracks" title bar and no year selector.
- **Data flow:** select artist, then `albums artist_id:N tags:ajlyKSswXWiq sort:yearalbum`, then `titles album_id:N tags:…` per visible album (lazy, prefetch two albums ahead), cover via `/music/<artwork_track_id>/cover_WxH_o.jpg`.
- **Actions:** Enter = play (replaces queue, not gapless), `a` = add to end, `n` = play next, on a track, album or artist. Selection modes: multi-select with Space.

## 3. Feasibility and risks

| Item | Verdict |
|---|---|
| Full-screen alternate buffer, resize, tabs, split panes, mouse clicks | Standard TUI features; fine in Terminal.Gui v2 (not verified in code yet). |
| Virtualised long lists (1 004 artists, 2 008 albums) | Needs an owner-draw / virtual list. Plan for a custom view for the album blocks. Terminal.Gui ListView virtualisation details were not confirmed from docs. |
| Server-side cover resizing | ✔ Verified: `/music/<id>/cover_96x96_o.jpg` returns a 6 KB JPEG in ~45 ms; `cover_48x48_o.png` a PNG; `cover_300x300_o` 43 KB in ~150 ms. No local resizing needed. |
| Cover rendering in the album list | **Sixel is a poor fit for scrolling lists**: images can't be clipped at a view edge, each one is re-sent on redraw, and overlapping images can flicker ([mintty issue](https://github.com/mintty/mintty/issues/905)). Windows Terminal also emulates a fixed 10x20 cell for Sixel ([PR 17421](https://hn.jakechampion.name/reader/https://github.com/microsoft/terminal/pull/17421)), which can make size calculations from a real cell-size query wrong. **Measured in Step 1:** half-block 16x8 covers look grainy; **quadrant blocks (2x2 samples per cell) at 24x12 cells** are noticeably sharper and are the current default, braille (2x4) adds dotty noise, not detail. Any cell mode is limited to 2 colours per cell, so it stays grainy. **Sixel was then implemented and is clearly the best** (240x240 px list covers in Windows Terminal), with partly scrolled covers falling back to cells; this supersedes the "Sixel only for the footer" recommendation below. Risks still open: possible flicker of the footer cover (resent at 10 fps), and terminals without Sixel. **Original recommendation:** draw list covers as true-colour **half-block cells** (`▀`, two pixels per cell), which scroll and clip like text, and optionally use Sixel only for the single, static now-playing cover in the footer. |
| Terminal.Gui image support | `ImageView` prefers Sixel when detected and otherwise falls back to coloured cells; it re-sends Sixel data only when marked dirty ([API](https://gui-cs.github.io/Terminal.Gui/api/Terminal.Gui.Views.ImageView.html)). `SixelEncoder` and `SixelToRender` exist, with a warning to add only on Sixel-capable terminals ([SixelToRender](https://gui-cs.github.io/Terminal.GuiV2Docs/api/Terminal.Gui.SixelToRender.html)). The README still recommends the Alpha channel for new projects, so API churn is a risk. |
| Two-column track lists | Custom drawing. Easy. |
| Waveform in the footer | Braille/half-block characters from cached peaks; the playhead moves at about 10 fps. See `research.md` section 4. |
| Image decoding in .NET | Covers arrive as JPEG/PNG; need a cross-platform decoder (ImageSharp or SkiaSharp; ImageSharp uses a split licence, so check it fits). Not yet researched in depth. |
| Minimum size | Layout needs roughly 100x30. Below that, hide covers or the waveform rather than the controls. |

## 4. Interaction sketch
- Global keys: `/` search, `Tab`/`Shift+Tab` and `1`/`2`/`3` switch tabs, `Space` play/pause, `n`/`p` next/prev, `←`/`→` seek ±10 s, `+`/`-` volume, `Ctrl+Q` quit.
- Mouse: click to focus, seek bar, buttons. The wheel scrolls whichever pane is under the pointer, focused or not.
- Scrollbars: every scrollable pane (artist list, album pane, search results) reserves its last column for a scrollbar, shown only when the content overflows; clicking the track jumps there (thumb dragging not implemented).
- Focus order: header search, tab strip, active tab panes, footer controls (global shortcuts work regardless of focus).

## 5. Questions for you
1. **Queue tab:** you listed Search and Library. Should custom queues have their own tab (and where do "Add to queue" results appear)? Is Queue a third tab, or a side panel?
2. **Left pane dimensions:** only Album Artist, or also Artist, Genre, Year, Composer, Folder?
3. **Covers:** OK with half-block (low-res but scroll-safe) covers in lists and Sixel only for the footer cover, or must everything be Sixel? Which terminal do you use (Windows Terminal assumed)?
4. **Search tab:** what should it show (grouped Artists / Albums / Tracks? one flat list?) and should results play on Enter?
5. **Multiple players:** show a player selector in the header, or always the one piCorePlayer?
6. **Footer size:** is 5–6 rows acceptable, or do you want a taller footer for a larger cover and waveform?

## 6. Implemented in Step 3
- **Left pane:** a one-row header shows the dimension (`Album Artist ▾`, `F6` hint); click it or press `F6` to switch Album Artist, Genre, Year. Each list starts with "All ..." and shows album counts; type-ahead jumps by name (ignoring a leading "The ") or year. The Album Artist list contains only artists with albums (LMS's `artists` also returns track-only people). Genre needs track data, so it fills in as a first sync progresses.
- **Right pane:** albums of the selection, each with cover, title, total length, `[artist ·] year · genres` and the two-column tracklist, all from the catalogue. While tracks have not arrived the block says `Loading tracks…`; visible albums refresh from the server as they appear. In lists that mix artists (All Artists, Genre, Year) the artist name is part of the detail line. Covers are fetched when a block becomes visible, with a dim `…` placeholder meanwhile (and `♪` for an album without artwork).
- **Tab strip, right side:** questions and notices. `Library not synced yet (N tracks, M albums). Sync now? [Y Sync now] [L Later]`, `Resync the library now?`, `A sync is running. Cancel it?`, and failure or cancellation reasons. Answer with the key, Enter (first answer), Esc (last answer) or a click. A notice never replaces a question and disappears after 5 s.
- **Header status:** the six states listed in the README, with sync progress and the age of the index; clicking it equals `F5`.
- **Empty states:** `Library not synced yet. Press F5 to sync.`, `Syncing the library…`, and `The server cannot be reached and there is no saved library yet. Press F5 to retry.`
- **Not done:** Space multi-select and a real queue (Step 5), the `a` / `n` actions only show a notice, an A-Z rail (type-ahead covers the jump), and the Folder dimension.

## 7. Implemented in Step 4 (Search tab)
- **Layout:** four groups, in this order: Artists (2), Albums (4), Tracks (10), Genres (3), each with a header `Name (shown / total)` and a `More …` row that expands it. Artists, albums and tracks are two-line rows with a 4x2 thumbnail on the left (artists: an icon block, since LMS has no portraits); line 1 is the name or title, line 2 (dim) is artist, album or year, the track length is right-aligned. Matched fragments are highlighted in both lines.
- **Typing:** `/` focuses the search box; every keystroke updates the results at once (local index). While the index is incomplete the server fills in, and a line `Searching the server for …` shows until it answers. `Enter` or `Down` moves to the results.
- **Results:** arrows, PageUp/PageDown, Home/End and the wheel move through them; `Enter` or a click plays a track or album (fake player until Step 5), opens an artist or genre in the Library tab; `a` / `n` add or play next (stubs until Step 5).
- **Esc** clears the search and returns to the tab you came from (from the box or from the results).
- **Not done:** playing a server-only track whose album is not in the index; Space multi-select.

## 8. Implemented in Step 5 (playback, Queue tab, waveform, help)
- **Footer:** shows what the player really plays (title, artist, album, cover), the seek bar with elapsed and total time (click to seek), transport buttons, volume bar and the waveform of the current track with the played part highlighted. While the waveform is being prepared the line says `Analysing waveform…`; if it cannot be drawn it says why (`No waveform: …`). When there is no player or the server is unreachable the footer's first line says so instead of `Nothing playing`.
- **Controls:** every control acts on the server at once and is shown immediately; the real answer replaces it a moment later. `Previous` after 3 seconds restarts the track.
- **Queue tab:** the live queue with a marker on the current track (`▶` playing, `▮` paused), counts and time left. `Enter` plays an entry, `x` / `Delete` removes, `Shift+Up/Down` moves, `c` clears (asks first). `s` saves the queue under a name (a text bar appears at the bottom), `o` lists saved queues: `Enter` replaces the queue (asks first, because the stream restarts), `a` appends, `x` deletes, `Esc` goes back. Saved queues keep file addresses, so they survive a rescan; files that disappeared are skipped and counted in the notice.
- **Queueing from the library and search:** `a` / `n` add the highlighted track to the end / right after the current track (playback continues without a gap); `A` / `N` do it for the whole album; `Enter` plays and replaces the queue.
- **Help:** `?` draws the key reference over the tabs; `?` or `Esc` closes it.
- **Not done:** a player selector, themes and key bindings in a settings file, a first-run screen.

## 9. Recently added tab (key 4)
- A fourth tab next to Search, Library and Queue. It uses the Library's album list (cover, title, artist, year, genres, track columns, `Enter`, `a` / `n`, `A` / `N`, F3 / F4) over the newest 50 albums, newest first, full width (there is no left column: nothing to group by).
- The order comes from the server (`albums … sort:new`, the time the files were added); the content comes from the local index. Opening the tab, or pressing `4` on it, asks the server again. Albums added after the last sync are not listed until you resync (F5).
- While the order is being fetched the tab says so; if the server does not answer it says that instead of showing an empty list.
