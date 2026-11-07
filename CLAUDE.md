# cLiMS

.NET 10 full-screen terminal client (Terminal.Gui) for Lyrion Music Server. See `README.md` and `docs/`.

## Coding standards

- **One type per file.** Every top-level C# class, record, struct, enum and interface lives in its own file, named after the type (`CoverKey` is in `CoverKey.cs`). This applies to `src` and `tests`: test helpers and fakes get their own files too. Nested types stay inside their parent.
