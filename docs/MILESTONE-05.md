# Milestone 05 — Disc inspection and automatic artwork

## Included
- Keeps CUE sheets as the parent library item and refreshes track metadata from the CUE on re-import.
- Counts CUE TRACK declarations, including data and audio tracks, and exposes per-mode counts.
- Serial scanner now searches only CUE-declared data-track files (or the raw image when no track map exists), reads a larger initial window, and normalizes underscores to hyphens.
- Automatic artwork lookup against Libretro's `Sony_-_PlayStation/Named_Boxarts` directory using the Git Trees API rather than the truncated GitHub web listing.
- Artwork candidates are title-matched and region-scored; multiple candidates open a preview picker.
- Downloaded art is validated as PNG and converted to a 170 × 170 PNG in the local cover cache.
- Automatic/manual artwork source metadata is persisted. Manual covers are not overwritten by automatic lookup.
- Artwork/network errors are non-fatal and do not cancel a successful game import.

## Important limitations
- Serial identification is best-effort byte scanning, not a complete ISO9660 `SYSTEM.CNF` parser. Some discs may still report no serial.
- Filename matching cannot guarantee a correct match for every unusual title, translation, demo, or compilation. The multi-result picker is provided to reduce incorrect assignments.
- GitHub API unauthenticated rate limits and repository availability can affect artwork lookup. A failed lookup leaves the game imported and available for manual cover selection.
- The source project targets .NET 10 WPF x64. Build and runtime validation must be performed in Visual Studio on Windows; this environment does not have the .NET SDK installed.

## Windows test checklist
1. Build `PSXperia.Remastered.sln` in Visual Studio using x64 / Release.
2. Import a CUE with one data track and two audio tracks. Confirm the row reports `3 tracks • 1 data / 2 audio`.
3. Re-import the same CUE. Confirm the library still contains one game and track metadata remains populated.
4. Confirm a multi-match title opens the artwork picker, previews are visible, and Skip leaves the game cover unchanged.
5. Apply an artwork choice and confirm it is saved as 170 × 170 PNG.
6. Set a manual cover, re-import, and confirm it is preserved.
7. Disable network access and import a game. Confirm import succeeds even though artwork lookup fails.
8. Confirm the dashboard count equals the number of visible game rows.

## Milestone 05.1 hotfix
- Selecting a BIN/IMG that is already referenced by an imported CUE now resolves to the existing CUE game entry instead of attempting a second standalone import.
- Import normalization no longer assumes an entry must survive legacy duplicate consolidation; it returns the surviving canonical entry when available and safely falls back otherwise.
- Artwork selection now handles a stale/missing preview match without throwing `Sequence contains no matching element`.
- These are defensive fixes based on the reported CUE-then-BIN import sequence and the observed `Sequence contains no matching element` dialog.
