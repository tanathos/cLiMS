# Clims.Waveform

Overview waveforms for the player's footer: downloads the original file of a track from LMS, decodes it inside .NET and reduces
it to about 1 200 peaks. UI-free; depends on `Clims.Lms` (for the download URL) and three small managed decoders.

| Type | Role |
|---|---|
| `PeakAnalyzer` | `Analyze(Stream)`: recognises the file by its first bytes (looking past an ID3 tag), decodes FLAC, MP3, Ogg Vorbis or WAV and returns a `PeakProfile` (peak per equal slice, normalised so the loudest slice is 1, then bent with a 0.7 power so quiet passages stay visible in two rows of text). Anything else throws `UnsupportedAudioException`. |
| `WaveformService` | Wanted tracks in, profiles out. `Want(current, next)` loads the current track first and the next queue entry ahead of time, one download at a time; a download for a track nobody wants any more is cancelled; profiles are cached in memory (64); `StateOf` / `ErrorOf` say why a track has none. |

## Rules
- **Read-only on the server.** A plain HTTP `GET /music/<id>/download`, no effect on playback. The file goes to a temporary file (deleted afterwards) and is never kept.
- **States:** `Loading`, `Ready`, `Unsupported` (an unknown format or a file over 400 MB: never retried) and `Failed` (network or decode error: retried after 30 s).
- Decoders: `BunLabs.NAudio.Flac` (MS-PL), `NLayer` (MIT), `NVorbis` (MIT), `NAudio.Core` (MIT). No native code, no ffmpeg.
- A 3.5-minute MP3 took 2.8 s end to end over the LAN (download plus decode); FLAC files are larger, so a few seconds more.

## Tests
`tests/Clims.Waveform.Tests` (13 unit tests plus 1 opt-in): synthetic WAV files (quiet then loud, stereo, silence), unsupported formats, cancellation, and the service over a fake HTTP handler (order, caching, no retry for unsupported, retry after the pause with a fake clock, cancellation of a running download, cache eviction, temp files removed). With `CLIMS_LMS_URL` set, one test downloads a real track and checks its length and that it is not silent.
