# Windows test plan — Milestone 04.1

Use a copy of the current project and keep a backup of the library JSON.

## Existing D (USA) (Disc 1) test

1. Launch the app. The legacy CUE and its `(... Track 1/2/3).bin` records should appear as one game row, not four.
2. The row should show 3 tracks, 1 data track, 2 audio tracks, and USA (based on the filename if the serial scan finds nothing).
3. If a serial is found in the first 32 MiB of the data track, it should appear under the region/track line. If not, `Serial not found` is expected and import must still succeed.
4. Re-import the CUE only. The row should refresh; it must not create three extra games.
5. Re-import with the CUE and all three BIN files selected. It must still create/refresh one game only.
6. Set a 170x170 cover. It should remain attached to the one game row after closing/reopening the app.
7. Move the disc folder, re-import the moved CUE, and verify the old duplicate game row is consolidated while preserving the cover.

## Regression checks

- Import a standalone ISO: one game row, with no CUE track breakdown expected.
- Import a standalone BIN/IMG: one row and a warning that a CUE is preferred when available.
- Start with an empty library: app opens without creating an error dialog.
- Mark `library.json` read-only and launch: loading should work; saving may clear the Read-only attribute only when the app is explicitly saving.
- Keep the original library JSON backup until all checks pass.

## Build verification

Run **Build → Rebuild Solution** in the user's Windows Visual Studio installation before pressing F5. This environment does not have the .NET SDK, so the source package is not compiler-verified here.
