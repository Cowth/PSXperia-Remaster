# Milestone 02 — CUE grouping and cover-art foundation

## Implemented in this update
- CUE sheets remain the canonical entry point for multi-track discs.
- When users multi-select a CUE and its referenced BIN files, the BIN files are grouped under the CUE and are not imported as separate games.
- CUE inspection retains INDEX 00 and INDEX 01 timestamps, track mode, track number, and referenced file path.
- Library entries retain track counts and a filename-derived region hint (USA / Europe / Japan).
- Added manual cover-art selection and local storage.
- Uploaded cover art is center-cropped and resized to 170 × 170 PNG.
- Existing library JSON entries remain compatible because new metadata fields have defaults.

## Important limitations
- Region detection is a filename hint only. It is not serial-number or executable-based game identification.
- Automatic online cover lookup is not implemented.
- The app still does not convert PS1 images, generate the PSXperia image TOC, build ZPAK/APK packages, or install to Xperia Play.
- This change does not alter any conversion algorithm. There is no conversion engine in this baseline to preserve or validate yet.
- Build and runtime validation must be performed on Windows with the .NET 10 Windows Desktop SDK. This environment cannot run the WPF application.
