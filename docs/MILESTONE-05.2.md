# Milestone 05.2 — Library removal

## Change
- Added a compact minus button to each game row.
- Removal requires explicit confirmation.
- Removing a game deletes only its library record from `library.json`.
- Original CUE/BIN/IMG/ISO files are never deleted.
- The visible list, empty-state panel, and dashboard count refresh immediately after a successful removal.
- If saving fails, the UI keeps the game visible and reports the error.

## Manual verification
1. Import two games and confirm the count is 2.
2. Click the minus button for one game; choose No. Confirm nothing changes.
3. Click it again; choose Yes. Confirm only that entry disappears and the count becomes 1.
4. Reopen the application and confirm the removed entry stays removed.
5. Verify the original CUE/BIN files still exist in Windows Explorer.
6. Remove the last game and confirm the empty-library panel returns.
