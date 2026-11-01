# Milestone 04.1 — Disc grouping and identification foundation

## Implemented in source

1. **CUE/BIN grouping**
   - A CUE remains the primary disc entry.
   - Referenced track files are represented in the CUE entry's `Tracks` collection, not as separate games.
   - Legacy `Title (Track N)` entries can be merged into a matching CUE record. Exact track paths are preferred; if the CUE has moved, the legacy filename pattern and same-folder relationship are used.
   - Duplicate CUE entries with the same disc filename are consolidated, preferring the entry with usable track metadata and preserving a cover/serial/region from the other entry.
   - Re-importing an existing entry refreshes the UI row, including track counts.

2. **Game-level artwork**
   - CoverPath remains on `GameEntry`, not `GameTrackEntry`.
   - If a legacy track entry held cover art, normalization transfers that cover to its CUE parent before removing the track row.
   - The 170x170 PNG writer validates the image frame and writes to a unique temporary file before replacing the destination.

3. **Disc serial and region identification**
   - Added a bounded scanner for common PlayStation product-code families. It scans up to 32 MiB per referenced file and returns the first serial-shaped token it finds.
   - Region is inferred from known serial families or explicit labels such as USA/NTSC-U, Europe/PAL, and Japan/NTSC-J.
   - The scan is best-effort. A serial not found does not block import; it remains unknown rather than inventing a match.

## Not yet implemented

4. **Automatic metadata and cover matching**
   - A real provider must be selected and integrated. The common game databases have differing API keys, terms, rate limits, and image licensing. No API credentials are assumed and no unverified metadata is fabricated.
   - Manual cover selection remains available. A later metadata milestone will add title/serial editing and a provider-backed candidate picker with a manual override.

5. **Full PSXperia conversion engine**
   - Not yet implemented. See `CONVERSION-ENGINE-AUDIT.md` for the original format and packaging findings. We should port and test the image container first, then ZPAK, then the runtime APK patch/package flow.

## Verification status

- Source-level review and static checks only in this environment.
- A Windows .NET 10 SDK/compiler is not available here, so this ZIP has **not** been compiled or run under WPF. Do not treat it as a build-verified release.
- Before replacing the local working folder, back up `%LOCALAPPDATA%\\PSXperiaRemastered\\library.json` and the existing project folder.
