# Settings page: implementation plan

Goal: a settings page that edits everything in `settings.json`, plus new preferences, with the file reorganised as v2.
Branch: `v0.9.2-dev`. Conventions: one top-level type per file (`src` and `tests`), warnings as errors, PowerShell/Bash agnostic.

## 1. The v2 file

```json
{
  "Version": 2,
  "Servers": [
    {
      "Name": "Home NAS", "Url": "http://192.168.1.100:9000/", "Uuid": "3f2a…",
      "Username": "eros", "ProtectedPassword": "…", "PlayerId": "aa:bb:cc:dd:ee:ff",
      "Lyrics": {
        "Enabled": true, "OnlineLookup": false, "Providers": ["lrclib"], "MissRetryDays": 1,
        "Files": { "PathMap": {}, "ShareUser": null, "SharePassword": null, "ProtectedSharePassword": null }
      }
    }
  ],
  "Startup": { "Server": "3f2a…", "View": "Library", "AutoConnect": true },
  "Cache": { "Covers": { "Enabled": true, "Normal": { "TtlDays": 30, "MaxEntries": 0 }, "Big": { "TtlDays": 30, "MaxEntries": 50 } } },
  "UI": {
    "Covers": {
      "Footer": { "Style": "Sixel" },
      "Library": { "Style": "Quadrant", "Size": "Medium" },
      "FullScreen": { "Style": "Sixel" },
      "SixelFallback": "Braille"
    },
    "Waveform": { "Enabled": true, "Style": "Blocks" },
    "Library": { "ColumnWidth": 30, "Sort": "Artist", "RecentCount": 50 },
    "Mouse": true,
    "Theme": "Default"
  }
}
```

Decisions already taken:
- Cache is global (the cover database files stay per server). Lyrics stay per server.
- `Covers.Library` also drives Recently added; `Size` exists only there (`Small`, `Medium`, `Large`).
- Output is indented JSON. Enums are written as names.
- Migration: a file without `Version`, or below 2, is renamed to `settings.v1.json` and the converted file is saved as `settings.json`.

Items still to verify against the code before building (they are inferred): `UI.Library.*`, `UI.Mouse`, `UI.Theme`,
`Startup.View`, `Startup.AutoConnect`. Drop any that have nothing to drive.

## 2. Current state (what has to change)

- `AppSettings` is a flat record (`LmsUrl`, `ServerUuid`, `PlayerId`, `Username`, `ProtectedPassword`, `CoverCache`, `Lyrics`, `Waveform`),
  loaded in `Program.cs:47`, saved from `Program.cs:103` and `Session.cs:71,197`.
- `Session` takes `AppSettings` and reads the flat fields (`Session.cs:60-91`). `ProtectSharePassword` is static on `Session`.
- Cover style is one global: `CoverArt.Mode` (`CoverArt.cs:18`), cycled by a key in `MainWindow.cs:874`. Size is fixed per view.
- The waveform style arrives through a constructor argument to `MainWindow` (`Program.cs:113`).

## 3. Steps

Each step builds and passes tests on its own, so it can be committed alone.

### Step 1: settings model v2 (no behaviour change)
New files, one type each, in `src/Clims.Tui/Settings/`:
`AppSettings` (root, replaces the old record), `ServerSettings`, `StartupSettings`, `CacheSettings` (holds the existing `CoverCacheSettings`, moved),
`UiSettings`, `CoverStyles`, `CoverPlaceSettings`, `WaveformSettings`, `LibrarySettings`, `CoverSize` (enum), `StartupView` (enum),
and `LyricsFilesSettings`. `LyricsSettings` and `CoverCacheTier` move with it.
All have defaults, so a partial file deserialises. `JsonSerializerOptions` is shared and has `WriteIndented = true` and a string enum converter.
Convenience: `AppSettings.ActiveServer` (by `Startup.Server`, else the first, else null).

### Step 2: v1 to v2 migration
`SettingsMigration` (static, pure: v1 JSON text in, `AppSettings` out) and `SettingsStore` (file I/O: `Load`, `Save`, and the rename).
- Detect v1: no `Version` property, or a value below 2.
- Rename `settings.json` to `settings.v1.json`. If that exists, keep the old backup and use `settings.v1.1.json`, `.2`, and so on.
- Map: `LmsUrl`/`ServerUuid`/`PlayerId`/`Username`/`ProtectedPassword` into one `Servers` entry (Name from the host), old `Lyrics` into it (the four share keys into `Files`),
  `CoverCache` into `Cache.Covers`, `Waveform` into `UI.Waveform.Style`. All other keys take defaults.
- A plain `MusicSharePassword` is encrypted into `ProtectedSharePassword` during conversion. The plain value stays only in the backup.
- If the old file cannot be parsed, it is still renamed and defaults are used.
- Move `ProtectSharePassword` from `Session` into the store, so that it also runs when a server is edited.
- Tests: full v1 file, partial v1 file, no file, corrupt file, collision of the backup name, plain password protected, v2 round trip, indentation.

### Step 3: wire the app to v2
- `Program.cs`: load through `SettingsStore`, pick the server from `Startup.Server` (a `--lms` argument or the `CLIMS_LMS_URL` variable still wins and is not saved as a new server unless connected successfully).
- `Session` takes a `ServerSettings` plus the global `AppSettings` instead of reading flat fields. Saving a changed player id or uuid updates that server's entry.
- `ConnectModel` keeps its job; on success it upserts a `Servers` entry (matched by uuid, else by URL).
- Replace the flat-field uses listed in section 2. Existing tests move to the new types.

### Step 4: per-place cover styles
- Replace the global `CoverArt.Mode` by a style passed at the call site: footer, library/recent grids and the full-screen cover each ask `UiSettings.Covers`.
- `SixelFallback` is used when `SixelCovers.Available` is false (today every place falls back to quadrants).
- The F3 key (`MainWindow.cs:870-883`) cycles the style of the place that has focus for the session only; the settings page stores the default.
- `CoverSize` for Library and Recent: define `Small`, `Medium`, `Large` as cell dimensions of a grid cell (to be measured in `AlbumsView`) and apply them in the layout.
  This is the one step that changes visible layout; it needs screenshots in all three sizes.

### Step 5: other preferences
Only the ones confirmed in section 1.
- `Waveform.Enabled`: when false, the footer shows no waveform and the waveform service is not started.
- `Startup.View`, `Startup.AutoConnect`: honoured in `Program.cs` and `MainWindow` (`ShowTab(0)` at line 128).
- `UI.Library.*`, `Mouse`, `Theme`: each only if the code already has something to drive.

### Step 6: the settings page
- Opened by a key (proposal: F10, plus a mention in the help overlay) as a full-screen overlay built like the existing overlays (`AlbumInfoOverlay` is the pattern, with `ClickShield`).
- Tabs: Servers, UI, Lyrics, Cache, Startup. One type per tab and a `SettingsModel` that holds the editable copy.
- Controls: checkboxes, radio groups for enums, number fields with limits, a list with add/edit/remove for servers and for the path map.
- Passwords are never displayed; typing replaces them and the field is then stored protected.
- Servers tab: choose the active server, add (reuses the Connect dialog and discovery), edit, remove (with confirmation; its cover database is left alone).
- Changes apply when possible (UI styles, waveform) and otherwise on the next connect (a different server needs a new `Session`).
  Switching server from the page restarts the session; it never touches playback on the old server.
- Save on each change, so there is no Apply button. Escape closes.

### Step 7: documentation and release
- README: a Settings section with the v2 file, and a screenshot of the page.
- Help overlay: a line for the settings key and for clicking the waveform.
- CHANGELOG under Unreleased: the new settings page, the v2 file and migration (with the `settings.v1.json` backup note), the per-place cover styles.
- Release only on request.

## 4. Risks

- Step 4 is the largest and touches paint code that has been tuned (flicker, Sixel placement). Keep it separate and test it in a real terminal.
- The server switcher must stop and rebuild the `Session` cleanly (HTTP clients, event stream, catalog service, cover database).
- A shared `Save` called from several places could race; serialise it through `SettingsStore` with a lock and write to a temp file, then replace.
- Never write a plain password back to the canonical file.
