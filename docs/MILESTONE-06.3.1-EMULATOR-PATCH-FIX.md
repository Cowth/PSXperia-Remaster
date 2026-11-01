# Milestone 06.3.1 — Emulator patch handling fix

Fixes a confirmed bug in the bundled original PSXperia Java converter.

## Root cause
The original `CrashBandicootExtractor.patchEmulator()` created a `File` for the optional emulator patch and compared that `File` object to the empty string. For runtime profiles E7BCB6D5 and BB542581, `emulator_patch` is intentionally empty. The comparison was always false, so the original tool passed the `config` directory to JBPatch as if it were a patch file. On Windows this failed with `FileNotFoundException: ...\config (Access is denied)`.

## Fix
The converter now checks the actual `emulator_patch` string before creating a File. If it is empty, it copies the verified original emulator binary to the patched-emulator output path and continues with the original wrapper integration. If a patch name is supplied, it verifies that the patch is a real file before applying it. This preserves the original CRC validation and patch path for profiles that require a binary patch.

## Verification performed here
- Recompiled the modified Java class against dependencies in the bundled PSXperia JAR using OpenJDK 21.
- Replaced only `CrashBandicootExtractor.class` inside the original JAR; original resources, patch profiles, wrapper library, AAPT binaries, signing key resource, and remaining converter classes are retained.
- Confirmed the USA APK profile CRC is E7BCB6D5 and its profile intentionally has an empty `emulator_patch` value.
- ZIP archive integrity checked successfully.

## User test still required
Build the WPF solution in Visual Studio and rerun extraction/conversion with the same runtime. Full APK generation and device installation have not yet been verified.
