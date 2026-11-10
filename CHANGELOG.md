# Changelog
All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/). Below 1.0.0, minor versions may change behaviour.

## [Unreleased]

### Added
- Lyrics in the full-screen album view, under the album details. Synced lyrics highlight the line being sung and keep it in the middle; plain ones scroll (Up, Down, Page Up/Down, the wheel; synced lyrics follow the music again after 8 seconds). `[` and `]` shift the timing by half a second, `R` searches again, `L` turns online lookup on.
- Lyrics store: found lyrics are kept for good in `lyrics.db` (one file for all servers, never expired), so a song is never searched twice and stored lyrics show offline. A search that found nothing is remembered for a day (`Lyrics.MissRetryDays` in `settings.json`, 0 to search every time); enabling another provider searches again at once, and a provider that could not be reached leaves nothing behind.
- Lyrics from the track itself, tried before any provider and not needing online lookup: the lyrics in the file's tags (asked from the server through its lyrics tag; LRC text comes out synced, junk like `XXX|` is ignored) and `.lrc` / `.txt` files next to the track (named like the audio file, or an `.lrc` in the folder named with the title), located through the new `Lyrics.MusicPathMap` setting (the server's music folder as this PC sees it, for example `/mnt/Music` to `\\nas\Music`). A share that cannot be reached is not remembered as "no lyrics". A share that needs a login takes `Lyrics.MusicShareUser` and `Lyrics.MusicSharePassword`; the password is encrypted (Windows) and removed from `settings.json` at the next launch.
- Lyrics providers: LRCLIB (synced, free, no key), DarkLyrics (metal, plain text, one page fills the whole album) and lyrics.ovh (plain text). Online lookup is off by default and is turned on with `L` in the album view or `Lyrics.OnlineLookup`; only the artist, album, title and length are sent. See the new "Lyrics" section of the README.

- Album view progress bars at the bottom: the playing track's, and, when the queue has more than one track, the whole queue's. The queue bar has one stretch per track, as long as the track and separated by a constant gap (drawn in half cells for precision); when there are too many tracks for gaps, the stretches touch and alternate between four shades of the accent colour. The queue's total length ends the bar.
- Queue tab: `X` removes every entry after the selected one.
- Hover highlight in light gray on clickable things: the tabs, the "⇄ Server" button and status icon, the transport buttons, the artist and album links in the player, and the artist, genre and year links on album lists. (It needs a terminal that reports mouse movement, such as Windows Terminal.)

### Changed
- The album view now covers the whole screen. The cover has a one-character margin on the left and one row above and below; the artist starts on the cover's first row, and the album's year sits next to its title. A "Track" line was added to the details, and "Runtime" shows the length of the current track (it showed the whole album's).
- The album view's details (artist, album, genre, runtime, format) are anchored to the top of the cover and a little closer together, to leave room for the lyrics. Lines already sung fade out in three steps.
- Queue tab: the heading shows "Queue" on the left and "N tracks · total · remaining" on the right; the keys are listed on the last row, separated by dots.

### Fixed
- Queue tab: removing an entry no longer scrolls the list so that the selection ends up on the bottom row.

## [0.9.0] - 2026-11-09

### Added
- Cover cache: covers are kept in a SQLite file of their own (`covers-<server uuid>.db`) and filled lazily, so later launches show them without asking the server. The normal (300 px) and full-screen (900 px) covers each have a time to live and an optional entry limit in the `CoverCache` node of `settings.json` (on by default, 30 days; the full-screen covers keep the newest 50).
- Genres explorer, a fifth tab (key `5`): pick a genre and the next column lists the genres that share at least one track with it, pick again to narrow further. The artists of the tracks tagged with the whole chain are listed underneath, and a random queue of 25, 50 or 100 of those tracks can replace the queue or be appended. Because a track can carry several genres (the track listing reports only one), the sync now also asks the server for the tracks of each genre (about 40 s more on a 20,000-track library) and the index stores those links. An index saved before this keeps working (each track has its one genre then); resync with F5 to get them all.
- Genres explorer, bottom panel: "Items tagged Death Metal + Thrash Metal · N artists · N tracks". The artists are shown first; click "N tracks" to list the tracks instead (artist · album · title, with how many genres each carries), click "N artists" to go back. It stays empty until a genre is chosen and takes the bottom third of the tab. The filter uses the same ⌕ icon as the Library (or `Ctrl+F`).
- The search box shows a dim "Search..." hint while it is empty and not focused.
- The terminal title follows the player: `cLiMS ▶ - Track name` while playing, `cLiMS ⏸ - Track name` while paused, `cLiMS` with nothing loaded.

### Changed
- Releases ship two builds per platform: the default self-contained one is now compressed (about 45 MB instead of 96 MB), and a new `-framework-dependent` one (about 26 MB) needs the .NET 10 runtime installed.
- Digit keys switch tabs even when a list with type-ahead has focus, unless a jump is in progress (they used to be typed into the list's type-ahead).
- The `?` help scrolls (arrow keys, Page Up/Down, Home/End, the wheel) when it does not fit the window, and lists the Genres tab keys.

### Fixed
- Covers (Sixel images) no longer stay missing after the terminal window is resized: once the size stops changing they are sent to the terminal again.
- Clicking the "5 Genres" tab (or any tab) puts the focus on its page; it used to leave it in the search box.
- Dragging a scrollbar no longer selects whatever is under the pointer when the button is released, in the Library, Recently added, Search and Genres lists; and a scrollbar drag that passes over the Library's divider no longer starts resizing the column.
- In the Genres tab, clicking a genre no longer scrolls its column so that the genre ends up on the last row, and switching to another tab and back keeps the whole chain of chosen genres (only the first one was kept).
- Clicking a column title in the Genres tab no longer picks a genre, and no genre looks highlighted while nothing is chosen.
- The CI timing test for the local search on 20,000 tracks no longer fails on a busy runner (it takes the fastest of several runs).

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
[0.9.0]: https://github.com/tanathos/cLiMS/releases/tag/v0.9.0
