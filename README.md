# cLiMS

Full-screen terminal client for Lyrion Music Server (LMS), built on .NET 10 and Terminal.Gui v2.
Planning and research live in [`docs/`](docs); start with [`docs/sprint.md`](docs/sprint.md).

## Status
- Step 1: TUI skeleton (`src/Clims.Tui`), done.
- Step 2: LMS client library (`src/Clims.Lms`, [README](src/Clims.Lms/README.md)), done.
- Step 3: local catalogue, Library tab on your real library, covers, sync status (`src/Clims.Catalog`, [README](src/Clims.Catalog/README.md)), done.
- Step 4: ranked search on every keystroke over the local index (about 2 ms, worst 15 ms on 20 000 tracks), with the server filling in while the index is incomplete, done.
- Step 5: **real playback** on your player (status polled every second and on server events, play/pause/seek/volume/next/previous), the live **queue** (jump, remove, reorder, clear), named **saved queues** kept on this PC, a **waveform** of the current track decoded from the real file (`src/Clims.Waveform`, [README](src/Clims.Waveform/README.md)), `?` help, a log file, single-file packaging and a CI workflow, done. Not verified: audio-level gaplessness (needs a listening test by you) and the controls against your real player (my checks were read-only so as not to interrupt your music; the commands are covered by fake-server tests).

Run the tests: `dotnet test` (add `$env:CLIMS_LMS_URL = "http://host:9000/"` to also run the read-only integration tests, including a full sync that takes about 1.5 minutes per test; add `$env:CLIMS_CATALOG_DB = "<path to catalog-*.db>"` to time the local search on your saved index, no server needed).

## Run
```
dotnet run --project src/Clims.Tui -- --lms http://192.168.1.20:9000
```
The address is remembered once the server has been reached, so afterwards `dotnet run --project src/Clims.Tui` is enough. Needs a true-colour terminal; Windows Terminal is the reference (Sixel covers need 1.22 or later). Minimum comfortable size is 100x30; below that the footer drops the cover and waveform but keeps the controls.

**First run:** the app shows the UI at once, then asks `Library not synced yet (N tracks, M albums). Sync now?` in the tab strip. Answer `Y` (or click). It only ever reads from the server; nothing is synced without your answer. A sync takes about 1.5 minutes on a Raspberry Pi; the Library tab is usable after the first ten seconds (artists and albums), and albums on screen fetch their own tracks meanwhile. The index is saved in `%LOCALAPPDATA%\cLiMS\catalog-<server uuid>.db` (safe to delete; it is only a cache), so later launches start in a few seconds.

**After that:** if the server's last scan or counts changed, the status says `Server changed, resync pending` and you are asked once; `Later` asks again at the next launch. A finished scan that changed nothing does not ask.

Options: `--lms <url>` (also `CLIMS_LMS_URL`), `--player <id>` (the player to control, usually its MAC address; default: the first connected hardware player; can also be set as `PlayerId` in settings.json), `--demo` (built-in fake library and player, no server; also what you get with no address at all), `--driver windows|dotnet|ansi` (force a Terminal.Gui driver).
Environment: `CLIMS_PERF=<file>` writes per-view paint timings on exit. The log (crashes, swallowed paint errors, failed player commands, start and stop) is `%LOCALAPPDATA%\cLiMS\clims.log` (cut to its newest half past 1 MB); nothing is ever printed to the console while the UI runs. Settings live in `%LOCALAPPDATA%\cLiMS\settings.json`, saved queues in `queues.json` next to it. Password-protected servers are not supported by the app yet (the library supports Basic auth, but covers and downloads would need the header too).

## Header status
`○ Not synced` · `◐ Sync in progress, live search only · artists ✓ albums ✓ tracks 42%` · `● Synced (2 h ago)` · `◔ Server changed, resync pending` · `✕ Offline (using local index)` · `◇ Demo data (no server)`. Click it, or press `F5`, to resync or cancel a running sync.

## Keys and mouse
| Key | Action |
|---|---|
| `/` | Focus the search box (typing switches to the Search tab and searches at once; `Esc` clears and returns to the previous tab, `Enter`/`Down` jumps to results) |
| Search results | Arrows, PageUp/PageDown, Home/End, wheel; `Enter` or double click opens or plays; a single click only selects; `a` / `n` queue the highlighted track; `Esc` back |
| `1` `2` `3` | Search / Library / Queue tab (click the tab strip too) |
| `Tab` | Move focus between panes |
| letters | Type-ahead in the left list (artist, genre or year) |
| `F6` | Switch the left list: Album Artist / Genre / Year (or click the ≡ icon in its header); click the ▾/▴ arrow to sort ascending/descending; drag the divider to resize the column (22 characters to half the screen); the ⌕ next to "All ..." (or `Ctrl+F`) turns that row into a filter field, `x` or `Esc` closes it |
| `Enter` or double click | Play the selected track, album or search result: replaces the queue and restarts the stream (not gapless) |
| `a` / `n` | Add the highlighted track to the end of the queue / play it next (gapless); `A` / `N` do the same for the whole album |
| Queue tab | `Enter` play that entry, `x` or `Delete` remove, `Shift+Up/Down` move, `c` clear (asks), `s` save under a name, `o` saved queues (`Enter` replace after asking, `a` append, `x` delete, `Esc` back) |
| `?` | Key reference |
| `Space` | Play / pause |
| `Left` `Right` | Seek -10 s / +10 s |
| `Alt+n` `Alt+p` | Next / previous track (work everywhere, also while typing) |
| `+` `-` | Volume |
| `F5` | Resync the library, or cancel a running sync |
| `Y` / `L` / `N` / `Enter` / `Esc` | Answer a question shown in the tab strip (`Enter` = first answer, `Esc` = last) |
| `F3` | Cycle cover rendering: Sixel (default, real pixels), half-block, quadrant, braille |
| `F4` | Toggle list cover size: 24x12 cells (default, 240x240 px as Sixel) / 16x8 |
| `Ctrl+Q` | Quit (`Esc` never quits; it only closes or goes back) |

Scrolling: the mouse wheel scrolls the pane under the pointer (no click needed). Lists show a scrollbar on their right edge; click it to jump.

Footer: click the progress bar to seek, the buttons to control playback, the volume bar to set it.

## Layout
- `src/Clims.Lms`: UI-free LMS client library (JSON-RPC, typed models, events). `tests/Clims.Lms.Tests`: its tests and recorded fixtures.
- `src/Clims.Catalog`: UI-free local catalogue: in-memory model, SQLite store, sync service and its state machine. `tests/Clims.Catalog.Tests`.
- `src/Clims.Tui`: the app. `Data/` has the player interface, the real player (`LmsPlayerController`), the fake player and demo catalogue, and the saved-queue store; `Ui/` the owner-drawn views (`PaintView` base, `RowList`, `AlbumsView`, `LibraryTab`, `SearchTab`, `ResultsView`, `QueueTab`, `HelpOverlay`, `FooterView`, `HeaderBar`, `TabStrip`, `MainWindow`), the cover pipeline (`CoverSources`, `CoverImage`, `CoverArt`, `SixelCovers`) and `AlbumRefresher`. `tests/Clims.Tui.Tests` covers the logic that does not need a terminal (including the player against a fake server).
- `src/Clims.Waveform`: UI-free waveform peaks of a track (download, FLAC/MP3/Ogg/WAV decode, cache). `tests/Clims.Waveform.Tests`.
- Packaging: `dotnet publish src/Clims.Tui -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` gives one `Clims.Tui.exe` (about 100 MB, no .NET needed; tried here, starts fine). `.github/workflows/ci.yml` builds, tests and publishes on every push (not run yet: the repository has no remote).

## Notes for contributors
- Terminal.Gui is pinned to 2.5.0 (stable on NuGet). The API is large and moves; check `Terminal.Gui.xml` in the NuGet cache instead of memory.
- A plain `View` container needs `CanFocus = true` or its children can never take focus.
- `View` already has `Activated`, `Frame` and `Title`; do not reuse those names for your own events, fields or properties (the compiler warns, and warnings are errors).
- Inside `Clims.Tui` use the alias `MusicCatalog` for `Clims.Catalog.Catalog`: the plain name resolves to the namespace.
- Never slice display strings by index: an emoji cut in half crashes Terminal.Gui. Use `PaintView.Put` / `Clip`, which handle it.
- Scroll panes: reveal the selection only when it changes (not every frame) or the wheel gets undone; reserve the last column for `PaintScrollbar`.
- Covers: views ask `CoverArt.Source` and draw a placeholder until the image arrives; `PaintView` reports what each view showed so the source can cancel the rest. Sixel cannot be clipped (only fully visible covers use it), is erased by any text written over it (views resend on repaint), and must be released by hidden views (`SixelCovers.Release(this)`). Terminal.Gui detects Sixel support about 2 s after start, so repaint when it appears.
- LMS quirks that bit us: `titles` with the genre tag only works with an explicit `sort:albumtrack`; `artists` includes track-only people; ids can change after a rescan.
- Anything that cancels work by "no longer wanted" needs a re-check after acquiring the slot: a freed slot can start the request that was just cancelled (two races found this way).
- Warnings are errors.
