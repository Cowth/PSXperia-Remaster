# Milestone 06.3 — Original APK Build Pipeline

## What changed
- Replaced the payload-only converter call with an adapter that invokes the original PSXperia command-line engine.
- Runs the original `extract` phase against the user's own Crash Bandicoot APK and matching standalone ZPAK.
- Runs the original `convert` phase using the extracted runtime data, preserving the original CRC-specific patching, image patch format, PSImage/TOC generation, ZPAK creation, AAPT APK rebuild, and signing behavior.
- Adds exact whole-APK CRC32 gating for the four supported patch profiles: `76C201D8`, `7C33475B`, `BB542581`, and `E7BCB6D5`. The original patch table also contains `3B6A23C1`, but explicitly marks it unsupported (`valid=no`).
- Validates the resulting APK, ZPAK metadata, image payload, and TOC before publishing output.
- Publishes into `PSXperia-<TITLE_ID>` under the selected output folder, retaining the original internal layout:
  - `com.sony.playstation.<TITLE_ID>.apk`
  - `data/com.sony.playstation.<TITLE_ID>/files/content/<TITLE_ID>.zpak`
  - `data/com.sony.playstation.<TITLE_ID>/files/content/image_ps_toc.bin`

## Runtime requirements
- Java must be installed and `java.exe` must be available on PATH.
- The user's own original Sony APK and matching external runtime ZPAK must be selected. These files are read by the extractor and are not copied into the distributable app.
- The bundled original PSXperia tool JAR is included with its GPL license notice in `tools/`. Review the license before redistributing the application.

## Important implementation choice
This milestone deliberately calls the original converter implementation instead of reimplementing APK resource decoding, binary XML string replacement, ARM binary patching, Android resource compilation, and legacy APK signing in C#. That is the closest way to preserve original converter behavior and avoid silently producing a structurally plausible but incompatible APK.

## Validation performed in this workspace
- Inspected original `PSXperiaTool.java`, `CrashBandicootExtractor.java`, `ApkBuilder.java`, `ZpakCreate.java`, and the original CLI argument handling.
- Confirmed original CLI starts and prints its help under OpenJDK 21.
- Confirmed supplied USA runtime APK whole-file CRC32 is `E7BCB6D5`, which matches the original supported USA profile.
- Confirmed generated-output validation checks APK manifest/DEX, ZPAK metadata and `data/image.ps`, and non-empty TOC.

## Not verified here
- The Windows WPF solution has not been compiled in this Linux workspace (no .NET SDK is installed).
- A full end-to-end conversion has not been run against the user's large runtime ZPAK and a full disc image in this workspace. The user must compile and run the milestone on Windows to verify AAPT and the original Java extraction/patch/sign sequence against their exact runtime files.
