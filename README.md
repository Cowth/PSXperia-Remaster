# PSXperia Remastered

A Windows desktop project for preparing PlayStation 1 disc images for the Sony Ericsson Xperia Play using the user's own compatible Sony runtime files.

> **Development status:** Milestone 06.7 — Xperia USB Connect (early test build). The desktop application has launched and compiled successfully in the maintainer's Visual Studio environment. Physical Xperia Play USB detection, APK installation, game-data transfer, and in-game launch still require device testing. Do not treat the USB workflow as production-verified yet.

## Project goals

- Modern Windows interface for the original PSXperia conversion workflow.
- Disc-image inspection and conversion for games the user legally owns.
- Local use of compatible Sony runtime APK/ZPAK files supplied by the user.
- Converted-game library, artwork, compatibility notes, and device workflow.
- Careful validation before claiming device installation or performance improvements.

## Current requirements

- Windows 10/11 x64.
- Visual Studio with the .NET 10 SDK and WPF workload.
- Java Development Kit 21 for the original conversion/signing pipeline.
- A compatible Sony runtime APK and matching ZPAK supplied by the user.
- Android SDK Platform-Tools (`adb`) for Xperia USB features.
- A compatible Xperia Play for physical-device testing.

## Build from source

1. Clone or download this repository.
2. Open `PSXperia.Remastered.sln` in Visual Studio.
3. Restore NuGet dependencies.
4. Build the solution for **x64**.
5. Configure Java 21 (`JAVA_HOME`) and Android Platform-Tools (`adb`) as required by the relevant workflows.
6. Launch the WPF application.

The public repository intentionally does **not** include Sony runtime APK/ZPAK files, PS1 game images, converted game packages, or other copyrighted user-supplied content. Supply compatible runtime files locally through the application.

## Important legal and preservation notes

- Use only game images you are legally entitled to use.
- Users must obtain and supply their own Sony runtime files. This project does not distribute Sony runtime APKs or ZPAKs.
- Do not submit game images, converted game data, Sony runtime files, BIOS/firmware dumps, or copyrighted cover scans to issues, pull requests, or compatibility reports.
- Product names and trademarks belong to their respective owners. This is an independent community project and is not affiliated with or endorsed by Sony.

## Third-party components

The `tools/PSXperiaTool.jar` component is accompanied by its Apache License 2.0 text in `tools/LICENSE-PSXperiaTool.txt`. Preserve that license and its required notices when redistributing the component. The license of the PSXperia Remastered application code itself has **not yet been selected**; no project-wide open-source license is granted by this repository draft.

## Repository structure

- `src/` — WPF application and supporting libraries.
- `tools/` — required third-party conversion tool and its license.
- `docs/` — milestone notes, technical audits, and test plans.
- `PSXperia.Remastered.sln` — Visual Studio solution.

## Testing status

See `docs/MILESTONE-06.7-XPERIA-USB-CONNECT.md`. The first physical-device test should be performed with USB debugging enabled and the user-authorized ADB host. Confirm device detection first; validate APK install, data placement, package verification, and game launch as separate steps.

## Contributing

See `CONTRIBUTING.md`. Contributions should focus on reproducible compatibility reports, safe performance diagnostics, user-interface improvements, and tests. Never attach copyrighted game/runtime files.
