# Changelog
All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/). Below 1.0.0, minor versions may change behaviour.

## [Unreleased]

### Fixed
- Linux: covers could not load (and 9 tests failed on Ubuntu CI) because the native `libSkiaSharp` was missing; `SkiaSharp.NativeAssets.Linux.NoDependencies` is now referenced.

## [0.7.0] - 2026-11-07
First tagged release.

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
