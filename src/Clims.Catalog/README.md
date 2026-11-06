# Clims.Catalog

The local catalogue of one LMS server: an in-memory model that the UI browses, a SQLite file that keeps it between runs,
and a service that decides when it is out of date. UI-free; depends on `Clims.Lms` and `Microsoft.Data.Sqlite`.

## Pieces
| Type | Role |
|---|---|
| `Catalog` | Read model: artists, genres, albums, tracks per album, album summaries (track count, length, genres), genre and year indexes, and the ranked search. Only one mutation, `ReplaceAlbumTracks`, and it is thread-safe. |
| `ICatalogStore` / `SqliteCatalogStore` | Save and reload. One file per server UUID (`CatalogPaths`), whole-index save in one transaction (a failure leaves the old index intact), schema version, a damaged or foreign file loads as "no index". |
| `ICatalogService` / `CatalogService` | Loads the index, compares it with the server, syncs when asked, refreshes single albums, follows server events. Raises `StatusChanged`, `CatalogChanged`, `PromptRequested`, `AlbumRefreshed` (any thread). |
| `Catalog.Search`, `SearchRanking`, `SearchQuery` | Ranked search over artists, albums, tracks and genres: words match anywhere in a name, any order, accents and case folded; tiers exact / starts with / word starts / inside, artist boost, totals per group. About 2 ms on 20 000 tracks. |
| `ISearchService` / `SearchService` | Local answer on every keystroke; the server's `search` fills the groups the index lacks (all of them with no index, only tracks while a first sync loads them), debounced 120 ms, stale answers dropped, an outage keeps the local result. |
| `TextFolding.Highlights` | Where the query matched in a display string, as ranges of the original text (for highlighting). |
| `SyncAssessor` | Pure rule: index versus live server (`lastscan`, track/album/artist/genre counts). |
| `SyncState` / `SyncStatus` | `NotSynced`, `Syncing`, `Synced`, `ServerChanged`, `Offline`, `Demo`, with progress text, age and a detail message. |

## Rules (from `docs/search.md`, all covered by tests)
- **Never sync automatically.** `StartAsync` loads the saved index and checks the server; it downloads nothing. The service raises `PromptRequested`; the app asks the user and calls `SyncAsync` (yes) or `DeferSync` (later).
- First build with no index asks once. A changed `lastscan` or changed counts give `ServerChanged` and one prompt. A finished scan that changed nothing (`rescan done` arrives twice even then) does not prompt. After "Later" there is no second prompt in this session, and one again at the next launch or when something new changes.
- A sync replaces the old index atomically. Cancel, failure or an outage leave the old index (and the file) untouched.
- A first build publishes a partial catalogue (artists, albums) as soon as it has them, so the library is browsable while about 20 000 tracks stream in (about 1.5 minutes on a Pi). Nothing partial is written to disk.
- While the server scans, a sync waits (the server is slow then and disconnects players at the end).
- **On-view refresh:** `RefreshAlbumAsync(albumId)` re-reads one album's tracks (with file URLs) and updates memory and disk. That is how edits made by targeted rescans, which no cheap signal reveals, show up where the user looks.
- Offline: the saved index keeps serving; the status says so.

## Server quirks that shaped this
- `titles` with the genre tag only works with an explicit `sort:albumtrack` on LMS 9.1.1 (`sort:title` or no sort: the connection is dropped). The sync query is pinned to it by a unit test.
- `artists` also returns people who only appear on tracks; the UI's Album Artist list filters to artists that have albums.
- Track ids and album ids can change after a full rescan, which is another reason the whole index is rebuilt rather than patched.

## Tests
`tests/Clims.Catalog.Tests` (about 115 unit tests plus 3 opt-in): model, text folding and highlights, ranking (tiers, ties, artist boost, totals, accents, punctuation, a 20 000-track timing bound), SQLite store (roundtrip, nulls and unicode, corruption, schema mismatch, atomicity), the assessor, the sync state machine and the search service (local only, server only, mixed, debounce, stale answers, outage) against a fake `ILmsClient`. With `CLIMS_LMS_URL` set, two read-only tests run a real full sync (counts match the server, SQLite roundtrip, a second launch asks nothing) and a real album refresh. With `CLIMS_CATALOG_DB` set to a saved index, one test times the search on real data (on a copy of the file).
