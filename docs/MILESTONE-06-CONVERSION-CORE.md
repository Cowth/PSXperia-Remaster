# Milestone 06 — Runtime validation and original-format conversion core

## What is implemented
- Runtime selection contract accepts a user-owned Crash Bandicoot APK and matching standalone ZPAK path.
- Runtime validator checks APK ZIP integrity, required ARM emulator libraries, nested default ZPAK metadata, external ZPAK metadata, required payload entries, and APK SHA-256 / CRC32.
- Runtime profile store remembers only the selected file paths under `%LOCALAPPDATA%`; it never copies proprietary runtime bytes into the application.
- PSImage writer follows the original container layout: 20-byte PSImage header, independently zlib-compressed `0x9300`-byte chunks, and little-endian TOC with compressed stream offsets.
- ZPAK writer emits relative forward-slash entry paths and STORED ZIP entries, matching the original converter's uncompressed ZPAK mode.
- Conversion engine validates the selected runtime and source image, writes `ZPAK/data/image.ps`, `image_ps_toc.bin`, and `<title-id>.zpak`, then verifies the generated archive contains the image payload.
- Failed conversion removes only its incomplete generated workspace; source disc and runtime files are read-only inputs.

## Important boundary
This is the first conversion-core milestone, **not yet a complete installable APK builder**. It does not yet perform the original CRC-specific emulator binary patch, wrapper replacement, XML/resource substitutions, Android APK rebuild/signing, or Android deployment. The UI is not yet wired to the runtime manager or conversion engine. The app must not label this payload-only output as a finished Xperia install package.

## Validation status
- Source-level structure reviewed against user-provided original PSXperia Java sources.
- The original `PSImageCreate` format and `ZpakCreate` behavior were inspected directly.
- This environment has no .NET SDK/compiler installed, so a Visual Studio/.NET build has **not** been run. Build and runtime validation are still required on the user's Windows machine.

## Next milestone
Wire Runtime Manager and Converter UI to these engine services, then implement the original extractor/patch-config pipeline and APK builder. Exact APK CRC32 must map to a known patch profile; unknown CRCs must fail safely rather than attempting a guessed patch.
