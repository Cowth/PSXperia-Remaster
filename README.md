# PSXperia Remastered

A Windows desktop project for preparing PlayStation 1 disc images for the Sony Ericsson Xperia Play using the user's own compatible Sony runtime files.

> **Development status:** V2 CLEAN development build, continuing Milestone 07.1. The repository includes the existing WPF application, original Java conversion tool, Game Library and artwork workflows, engine regression tests, and the read-only Runtime Archive Measurement utility. Physical Xperia Play USB detection, APK installation, game-data transfer, and in-game launch still require device testing; the USB workflow is not production-verified.

## Project goals

- Modern Windows interface for the original PSXperia conversion workflow.
- Disc-image inspection and conversion for games the user legally owns.
- Local use of compatible Sony runtime APK/ZPAK files supplied by the user.
- Game Library with manual 170 × 170 cover import, automatic artwork lookup, and converted-game artwork management.
- Suggested output Title IDs based on the selected game's detected serial, while keeping the field editable.
- Synthetic regression coverage for the conversion engine and read-only runtime archive measurement.
- Compatibility notes and Xperia device workflow.
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

For the verified development environment, exact Release x64 build and test
commands, local signing prerequisites, and frozen Java engine/security notes,
see [docs/DEVELOPMENT-HANDOFF.md](docs/DEVELOPMENT-HANDOFF.md).

The public repository intentionally does **not** include Sony runtime APK/ZPAK files, PS1 game images, converted game packages, or other copyrighted user-supplied content. Supply compatible runtime files locally through the application.

## Important legal and preservation notes

- Use only game images you are legally entitled to use.
- Users must obtain and supply their own Sony runtime files. This project does not distribute Sony runtime APKs or ZPAKs.
- Do not submit game images, converted game data, Sony runtime files, BIOS/firmware dumps, or copyrighted cover scans to issues, pull requests, or compatibility reports.
- Product names and trademarks belong to their respective owners. This is an independent community project and is not affiliated with or endorsed by Sony.

## Third-party components

The `tools/PSXperiaTool.jar` component is accompanied by an Apache License 2.0 text, but the recovered PSXperia sources contain GPL-3.0-or-later headers and the current JAR has not been proven reproducible from pinned inputs. Treat redistribution of the JAR as blocked pending legal review; do not assume the included license text resolves the conflicting provenance. See [docs/DEVELOPMENT-HANDOFF.md](docs/DEVELOPMENT-HANDOFF.md). The license of the PSXperia Remastered application code itself has **not yet been selected**; no project-wide open-source license is granted by this repository draft.

## Repository structure

- `src/` — WPF application and supporting libraries.
- `tools/` — required third-party conversion tool and its license.
- `docs/` — milestone notes, technical audits, and test plans.
- `PSXperia.Remastered.sln` — Visual Studio solution.

## Testing status

The solution includes the `PSXperia.Engine.Tests` xUnit suite, WPF Title ID selection tests, and the `PSXperia.RuntimeArchiveMeasure.Tests` synthetic regression harness. The measurement utility does not extract runtime archive entries and its reports are not compatibility verdicts. See [docs/DEVELOPMENT-HANDOFF.md](docs/DEVELOPMENT-HANDOFF.md) for Release x64 build and test commands.

The Java conversion engine remains frozen at the documented JAR hash. Java ZIP extraction and APKTool resource extraction security remain outstanding, and the bundled tool's provenance/licensing requires legal review. See the handoff document for details. Physical Xperia Play testing is still required for USB detection, APK installation, game-data transfer, and in-game launch; see `docs/MILESTONE-06.7-XPERIA-USB-CONNECT.md`.

## Contributing

See `CONTRIBUTING.md`. Contributions should focus on reproducible compatibility reports, safe performance diagnostics, user-interface improvements, and tests. Never attach copyrighted game/runtime files.
