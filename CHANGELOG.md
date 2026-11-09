# Changelog
All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/). Below 1.0.0, minor versions may change behaviour.

## [Unreleased]

## [0.9.0] - Unreleased

### Added
- Cover cache: covers are kept in a SQLite file of their own (`covers-<server uuid>.db`) and filled lazily, so later launches show them without asking the server. The normal (300 px) and full-screen (900 px) covers each have a time to live and an optional entry limit in the `CoverCache` node of `settings.json` (on by default, 30 days; the full-screen covers keep the newest 50).
- Genres explorer, a fifth tab (key `5`): pick a genre and the next column lists the genres that share at least one track with it, pick again to narrow further. The artists of the tracks tagged with the whole chain are listed underneath, and a random queue of 25, 50 or 100 of those tracks can replace the queue or be appended. Because a track can carry several genres (the track listing reports only one), the sync now also asks the server for the tracks of each genre (about 40 s more on a 20,000-track library) and the index stores those links. An index saved before this keeps working (each track has its one genre then); resync with F5 to get them all.

### Changed
- Releases ship two builds per platform: the default self-contained one is now compressed (about 45 MB instead of 96 MB), and a new `-framework-dependent` one (about 26 MB) needs the .NET 10 runtime installed.
- Digit keys switch tabs even when a list with type-ahead has focus, unless a jump is in progress (they used to be typed into the list's type-ahead).

### Fixed
- Covers (Sixel images) no longer stay missing after the terminal window is resized: once the size stops changing they are sent to the terminal again.

## [0.8.0] - 2026-11-08

### Added
- Album lists show a sort label at the top right of the heading row ("Year ▼" / "Year ▲"); click it to flip the direction.
- The artist, year and genres on an album's line are links (underlined): the artist opens in the Library, a genre opens the Genre view with that genre selected, the year opens the Year view with that year selected. They work on the Library and Recently added tabs.
- Click the cover in the player to open the playing album full screen: large cover on the left; artist, album title, genres, runtime in minutes and file format (with a lossless/lossy icon) on the right. The rest of the screen stays visible, dimmed. Esc, Enter, `q` or a click closes it; playback keys still work.
- Right-click an album cover or a track (in the Library, Recently added and Search tabs) for a menu with "Play" and "Enqueue". On a cover they act on the whole album, on a track on that track. Arrow keys, Enter, Esc and the mouse work in the menu.
- Multi-disc albums show a "Disc N" heading above each disc, and every disc's tracks are split into two columns of their own.
- The player shows the position in the queue, for example "(3/12)", at the end of the title line.

### Changed
- The Library's left column starts 6 characters wider (36). The Recently added tab shows the newest 50 albums instead of 100.
- Header: the server area shows only the player name and a coloured status icon (no "Synced (… ago)" text, no volume). Click the icon to see the status ("Synced 1 min ago, resync now?"). Only the icon and the "⇄ Server" button are clickable, and clicking empty header space no longer focuses the search box. The footer volume bar is labelled with a 🔊 icon instead of "Vol".
- Album lists are ordered by year, newest first, by default (albums without a year come last). Recently added keeps the server's newest-first order.
- The track title in the player is bold, and the waveform is drawn in softer colours.

## [0.7.0] - 2026-11-07
First tagged release.

### Fixed
- Linux: covers could not load (and 9 tests failed on Ubuntu CI) because the native `libSkiaSharp` was missing; `SkiaSharp.NativeAssets.Linux.NoDependencies` is now referenced.

### Added
- Library by Album Artist, Genre or Year with covers (Sixel, with half-block, quadrant and braille fallbacks), track lists, a resizable, sortable and filterable left column.
- Instant ranked search over a local SQLite index, with the server's own search filling in while the index is incomplete.
- Playback on an LMS player (play/pause, seek, volume, next/previous), kept in step through server events.
- Queue tab: reorder, remove, clear, and named saved queues kept on your PC.
- Waveform of the current track, decoded from the real file (FLAC, MP3, Ogg, WAV).
- First-run connect screen with server discovery and a manual address form; password-protected servers; `F2` / `⇄ Server` to switch servers; one saved index per server.
- "Recently added" tab (key `4`): the newest 100 albums from the server, newest first.
- `--version` option.

### Known limits
- Gapless playback is implemented by editing the queue in place but has not been verified by listening.
- Password-protected servers are tested against fakes only.
- Windows Terminal is the reference terminal. Linux and macOS builds are published but untested.
- The binaries are not code-signed (Windows SmartScreen will warn on first run).

[0.7.0]: https://github.com/tanathos/cLiMS/releases/tag/v0.7.0
[0.8.0]: https://github.com/tanathos/cLiMS/releases/tag/v0.8.0
