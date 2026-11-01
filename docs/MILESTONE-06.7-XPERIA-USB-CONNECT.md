# Milestone 06.7 — Xperia USB Connect (initial device workflow)

This milestone adds an ADB-backed first pass at physical Xperia Play testing.

## Included
- Finds `adb.exe` from `ANDROID_SDK_ROOT`, `ANDROID_HOME`, the common per-user Android SDK location, or `PATH`.
- Lists authorized Android devices and reads model/manufacturer/Android release properties.
- Adds a Device workspace with explicit Detect Xperia and USB-debugging help actions.
- Lets the user select a previously converted game, install its APK with `adb install -r`, transfer the matching `data/<package>/` directory to `/sdcard/Android/data/<package>/`, and verify the package with `pm path`.
- Requires confirmation before installation and reports each step separately.
- Does not download or redistribute Android platform-tools, Sony runtime files, APKs, or game data.

## Requirements before testing
1. Install Android SDK Platform-Tools from Google's official Android developer site.
2. Ensure `adb.exe` is available on `PATH`, or set `ANDROID_SDK_ROOT` to the Android SDK folder (the app looks under `platform-tools`).
3. On the Xperia Play, enable **Settings → Applications → Development → USB debugging**.
4. Connect with a USB data cable. Install a compatible Sony Ericsson USB driver on Windows if required.
5. If the phone asks whether to allow USB debugging, approve the computer. Re-open the app's Xperia Device page and click **Detect Xperia**.

## Initial test sequence
1. Build the solution for x64 in Visual Studio.
2. Connect the Xperia Play and confirm that the Device page reports its model and Android version.
3. Convert a legally owned test game using a locally supplied compatible Sony runtime.
4. Select that converted game on the Device page and click **Install to Xperia Play**.
5. Confirm the installation prompt. The app installs the APK, pushes the package data folder, and checks that Android reports the package path.
6. Manually launch the game on the phone and record boot, controls, audio, graphics, and performance observations in the Compatibility page.

## Important limitations / validation status
- This is an initial ADB workflow, not yet a verified end-to-end Xperia Play installer. No physical Xperia Play was connected in the development environment for this milestone.
- The app does not yet identify unauthorized devices as a separate status, display a live transfer progress bar, launch the game, uninstall packages, or confirm that the game data is readable by Sony's runtime.
- Android versions and vendor ROMs can differ in external-storage behavior. The target path `/sdcard/Android/data/<package>/` must be confirmed on the actual R800i before calling the workflow verified.
- `pm path` verifies package installation only; it does not prove that the game boots or that the transferred ZPAK/TOC is valid.
- Keep USB debugging disabled when it is not needed.
