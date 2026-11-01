# Milestone 06.2 — Converter UI and Runtime Manager

## Added
- The Dashboard's Converter navigation button now opens a dedicated converter workspace.
- Game selector is populated from the imported game library.
- Title ID is suggested from the detected disc serial, while remaining editable.
- Runtime Manager accepts a user-selected Crash Bandicoot APK and standalone ZPAK.
- Runtime validation reports required APK entries, metadata, SHA-256, CRC32, and errors/warnings.
- Valid runtime paths are saved under the user's Local AppData profile; runtime files themselves are not copied into the application.
- Output folder picker and conversion progress/status are wired to the conversion engine.
- CUE input is accepted by the conversion engine and resolves to its first non-audio data track; audio tracks are not mistakenly passed to the PSImage writer.

## Important current scope
This milestone is a usable UI for the existing conversion-core stage. It creates an intermediate PSImage, TOC, and payload ZPAK. It does **not** yet create a final installable APK/data deployment. The original workflow's CRC-specific Crash Bandicoot extraction/patch selection, game patch application, Android resource rebuild, APK signing, and final output directory assembly remain future work.

## Validation performed
- Parsed all project `.csproj` and WPF `.xaml` files as XML.
- Checked that the Converter click handler exists and is referenced by XAML.
- Checked that runtime manager calls the existing `RuntimeValidator`, `RuntimeProfileStore`, and `OriginalConversionEngine` APIs.
- Checked CUE resolution selects the first non-audio track.
- Verified archive contents after packaging.

A .NET compiler is not available in the authoring environment; the solution still needs a Rebuild in the user's Visual Studio installation.
