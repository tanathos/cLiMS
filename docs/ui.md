# cLiMS – UI research and layout proposal

Research only. Reference: the MusicBee-style "Album Artist" screenshot you provided. Terminal framework assumption: Terminal.Gui v2 (see `research.md` section 3).

## 1. Layout

Always full screen (alternate screen buffer, resize-aware). Three fixed regions:

```
┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│ cLiMS   🔍 [ search…                         ]      piCorePlayer ● Synced (2 h ago)  🔊 70   │ HEADER (1–3 rows)
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

- **Header (always visible):** search bar (global shortcut `/`), plus a small status area: player name, **library sync status** (`Not synced`, `Sync in progress, live search only`, `Synced`; also `Server changed, resync pending` and `Offline (using local index)`, details in `search.md` section 10) and connection state. Typing debounces about 250 ms and runs the LMS `search` command; results go to the Search tab, which the header switches to.
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
| Cover rendering in the album list | **Sixel is a poor fit for scrolling lists**: images can't be clipped at a view edge, each one is re-sent on redraw, and overlapping images can flicker ([mintty issue](https://github.com/mintty/mintty/issues/905)). Windows Terminal also emulates a fixed 10x20 cell for Sixel ([PR 17421](https://hn.jakechampion.name/reader/https://github.com/microsoft/terminal/pull/17421)), which can make size calculations from a real cell-size query wrong. **Recommendation:** draw list covers as true-colour **half-block cells** (`▀`, two pixels per cell), which scroll and clip like text, and optionally use Sixel only for the single, static now-playing cover in the footer. |
| Terminal.Gui image support | `ImageView` prefers Sixel when detected and otherwise falls back to coloured cells; it re-sends Sixel data only when marked dirty ([API](https://gui-cs.github.io/Terminal.Gui/api/Terminal.Gui.Views.ImageView.html)). `SixelEncoder` and `SixelToRender` exist, with a warning to add only on Sixel-capable terminals ([SixelToRender](https://gui-cs.github.io/Terminal.GuiV2Docs/api/Terminal.Gui.SixelToRender.html)). The README still recommends the Alpha channel for new projects, so API churn is a risk. |
| Two-column track lists | Custom drawing. Easy. |
| Waveform in the footer | Braille/half-block characters from cached peaks; the playhead moves at about 10 fps. See `research.md` section 4. |
| Image decoding in .NET | Covers arrive as JPEG/PNG; need a cross-platform decoder (ImageSharp or SkiaSharp; ImageSharp uses a split licence, so check it fits). Not yet researched in depth. |
| Minimum size | Layout needs roughly 100x30. Below that, hide covers or the waveform rather than the controls. |

## 4. Interaction sketch
- Global keys: `/` search, `Tab`/`Shift+Tab` and `1`/`2`/`3` switch tabs, `Space` play/pause, `n`/`p` next/prev, `←`/`→` seek ±10 s, `+`/`-` volume, `Ctrl+Q` quit.
- Mouse: click to focus, seek bar, buttons, wheel scroll.
- Focus order: header search, tab strip, active tab panes, footer controls (global shortcuts work regardless of focus).

## 5. Questions for you
1. **Queue tab:** you listed Search and Library. Should custom queues have their own tab (and where do "Add to queue" results appear)? Is Queue a third tab, or a side panel?
2. **Left pane dimensions:** only Album Artist, or also Artist, Genre, Year, Composer, Folder?
3. **Covers:** OK with half-block (low-res but scroll-safe) covers in lists and Sixel only for the footer cover, or must everything be Sixel? Which terminal do you use (Windows Terminal assumed)?
4. **Search tab:** what should it show (grouped Artists / Albums / Tracks? one flat list?) and should results play on Enter?
5. **Multiple players:** show a player selector in the header, or always the one piCorePlayer?
6. **Footer size:** is 5–6 rows acceptable, or do you want a taller footer for a larger cover and waveform?
