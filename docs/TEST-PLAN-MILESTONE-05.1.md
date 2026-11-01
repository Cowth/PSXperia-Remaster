# Milestone 05.1 — Regression test plan

1. Import a CUE referencing one data BIN and two audio BINs. Expected: one library entry; 3 tracks; 1 data; 2 audio.
2. Select/import one of those referenced BINs afterward. Expected: it resolves to the existing CUE parent; no second game entry and no unhandled exception.
3. Select the CUE and its referenced BINs together in one multi-select operation. Expected: the CUE is imported once and referenced BINs are skipped as standalone candidates.
4. Re-import the same CUE. Expected: existing entry is refreshed; no duplicate.
5. Import a genuinely standalone BIN not referenced by any library CUE. Expected: it can still be indexed as a raw image with a warning that a CUE is preferred.
6. During artwork selection, cancel/skip. Expected: game import remains successful and cover remains unchanged.
7. Choose an artwork preview. Expected: selected PNG is saved and associated with the game.
8. Set a manual cover and re-import. Expected: manual cover remains unchanged.

Build validation: open `PSXperia.Remastered.sln` in Visual Studio, select x64 / Release, then Build Solution. This package has not been compiled in this environment because the .NET SDK is unavailable here.
