# cLiMS

Full-screen terminal client for Lyrion Music Server (LMS), built on .NET 10 and Terminal.Gui v2.
Planning and research live in [`docs/`](docs); start with [`docs/sprint.md`](docs/sprint.md).

## Status
Step 1 (TUI skeleton with fake data, no network) is implemented. The LMS library, catalogue index, search and real playback are Steps 2 to 5.

## Run
```
dotnet run --project src/Clims.Tui
```
Needs a true-colour terminal; Windows Terminal is the reference. Minimum comfortable size is 100x30; below that the footer drops the cover and waveform but keeps the controls.

Optional flags: `--driver windows|dotnet|ansi` forces a Terminal.Gui driver.
`--lms http://host:9000` fetches 10 random real covers from your server (read-only requests, SkiaSharp decode) and uses them for every album in the fake library, to judge how real artwork looks. Temporary dev spike (`Data/DevCovers.cs`), removed in Step 3.
Environment: `CLIMS_PERF=<file>` writes per-view paint timings on exit.
Crashes and swallowed paint errors are logged to `%TEMP%\clims-crash.log`.

## Keys and mouse
| Key | Action |
|---|---|
| `/` | Focus the search box (typing switches to the Search tab; `Esc` clears, `Enter`/`Down` jumps to results) |
| `1` `2` `3` | Search / Library / Queue tab (click the tab strip too) |
| `Tab` | Move focus between panes |
| letters | Type-ahead in the artist list |
| `Enter` or click | Play the selected track, album or search result |
| `Space` | Play / pause |
| `Left` `Right` | Seek -10 s / +10 s |
| `n` `p` | Next / previous track (not in the artist list, where letters are type-ahead) |
| `+` `-` | Volume |
| `F2` | Cycle the fake sync status (Synced / Not synced / Sync in progress) |
| `F3` | Cycle cover rendering: Sixel (default, real pixels; needs a Sixel terminal such as Windows Terminal 1.22+), half-block, quadrant, braille. Only differs with real covers, see `--lms` |
| `F4` | Toggle list cover size: 24x12 cells (default, 240x240 px as Sixel) / 16x8 |
| `Ctrl+Q` | Quit |

Scrolling: the mouse wheel scrolls the pane under the pointer (no click needed). Artist list, album pane and search results show a scrollbar on their right edge; click it to jump.

Footer: click the progress bar to seek, the buttons to control playback, the volume bar to set it.

## Layout
- `src/Clims.Tui/Data`: models, the `ILibrarySource` and `IPlayerController` interfaces, and in-memory fakes (1 000 artists, about 2 500 albums). Steps 2 to 5 replace the fakes.
- `src/Clims.Tui/Ui`: owner-drawn views. `PaintView` is the base (clipped text helpers, half-block covers), `RowList` is the virtualised list, `AlbumsView` the variable-height album blocks, `FooterView` the transport bar and waveform, `MainWindow` the wiring and global keys.

## Notes for contributors
- Terminal.Gui is pinned to 2.5.0 (stable on NuGet). The API is large and moves; check `Terminal.Gui.xml` in the NuGet cache instead of memory.
- A plain `View` container needs `CanFocus = true` or its children can never take focus.
- `View.Activated` and `View.Frame` exist; do not reuse those names for events or fields.
- Never slice display strings by index: an emoji cut in half crashes Terminal.Gui. Use `PaintView.Put` / `Clip`, which handle it.
- Scroll panes: reveal the selection only when it changes (not every frame) or the wheel gets undone; reserve the last column for `PaintScrollbar`.
- Sixel covers (`SixelCovers.cs`): Terminal.Gui detects Sixel support asynchronously (about 2 s after start), so repaint when it appears. Sixel cannot be clipped, so only fully visible covers use it. A view must resend its sixels whenever it repaints the cells underneath (any `AddStr` over the image erases it); hidden views must `SixelCovers.Release(this)`.
- Warnings are errors.
