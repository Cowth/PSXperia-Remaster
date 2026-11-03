# Milestone 07.1 — Stage 0 Baseline

## Status

Stage 0 is recorded for review. Stage 1 limit-dependent implementation is pending
measurements from legitimate supported runtime archives. No archive limits are
selected in this document.

## Current bundled Java engine baseline

- JAR: `tools/PSXperiaTool.jar`
- SHA-256: `01857E1615DC4514F6DB65DABB8CA93ED8F893CCC1BD5307C59B21504500FBF8`
- Measurement environment: Windows x64, Eclipse Temurin OpenJDK `21.0.13+9-LTS`
  (`21.0.13.9-hotspot` distribution build).
- Main CLI class: `com.yifanlu.PSXperiaTool.Interface.CommandLine`.
- The current conversion engine invokes the JAR using `java.exe -cp` and the
  existing `extract` / `convert` CLI arguments.
- JAR class-file versions: 33 PSXperia classes are major version 50; the locally
  modified `ApkBuilder.class` and `CrashBandicootExtractor.class` are major
  version 65.
- Java custom extraction API: `java.util.zip.ZipInputStream`,
  `java.util.zip.ZipEntry`, `java.io.File`, and `java.io.FileOutputStream`, with
  Apache Commons IO `FileUtils.touch`.
- APKTool archive-reading API: `java.util.zip.ZipFile` through
  `brut.directory.ZipRODirectory`; resource output uses `brut.directory.FileDirectory`.
- C# runtime archive validation uses `System.IO.Compression.ZipArchive` and
  `ZipArchiveEntry.Open()`.

## Upstream source pins

- PSXperia source commit: `3be12b3ef25daf3d976bac9dac5a74fb709a46be`
  (`yifanlu/PSXperia`, the source revision recorded as the repository's latest
  commit when this baseline was established).
- Wrapper submodule gitlink: `a0dc02875fd8b0c0ca7ea517e357ee886c24b24d`
  (`yifanlu/PSXPeria-Wrapper`), as pinned by the PSXperia source repository.
- The exact bundled `libjava-activity-wrapper.so` binary has not been proven to
  have been built from that submodule commit.

## Preserved local Java modifications

1. `com.yifanlu.PSXperiaTool.ApkBuilder`: the bundled class builds and validates
   an unsigned APK for modern external signing. It has no upstream embedded
   signing method or keystore resource. This behavior must remain intact.
2. `com.yifanlu.PSXperiaTool.Extractor.CrashBandicootExtractor`: the bundled
   class supports profiles with an empty emulator-patch setting by copying the
   original emulator binary into the expected output path before adding the
   wrapper. This behavior must remain intact.

The bundled `CrashBandicootExtractor.extractZip()` implementation otherwise
matches the pinned upstream extraction behavior. This is the Stage 1 security
boundary; the APKTool resource-decoding path is outside Stage 1.

## Dependency versions observed in the JAR

- APKTool `apktool-lib` and `apktool-cli`: `1.4.1`
- `brut.j` `dir`, `common`, and `util`: `1.0`
- `dexlib`, `baksmali`, `smali`, and smali `util`: `1.2.6-brut5`
- ANTLR runtime: `3.2`
- SnakeYAML: `1.7`
- Commons IO embedded Maven metadata: `1.4`
- XPP3 declared in the embedded APKTool POM: `1.1.4c`
- The upstream README names Commons IO `2.0.1`; this differs from the JAR's
  embedded metadata and must be resolved before claiming a reproducible build.
- Other inputs named in the upstream build instructions include Android SDK
  `sdklib`, Swing layout, `jbdiff`, platform-specific `aapt` binaries, Android
  framework resources, and the compiled wrapper library. Their exact artifact
  hashes/build provenance are not established here.

## Runtime archive measurement gate

No legitimate APK or ZPAK archive is present in the repository. The only matched
archive artifact in the project is `tools/PSXperiaTool.jar`. Therefore Stage 1
entry-count and expanded-byte limits cannot yet be selected without guessing.

Before selecting limits, measure every supported legitimate runtime profile:

- Each of the four supported APK CRC profiles: `76C201D8`, `7C33475B`,
  `BB542581`, and `E7BCB6D5`.
- Each profile's matching external runtime ZPAK.
- The exact embedded default ZPAK selected from the APK's top-level `assets`
  directory.
- For each archive: compressed file length; central-directory entry count;
  normalized names; duplicate, case-colliding, and file/directory-conflicting
  paths; per-entry declared compressed/uncompressed sizes; measured actual
  expanded bytes; and aggregate expanded bytes.
- For each extraction role, identify entries accepted and skipped by the
  existing filters, and record the extracted path set to verify legitimate
  output-path preservation.
- Record nested archive depth and the embedded ZPAK's compressed and actual
  expanded sizes separately.

The resulting policy must use documented per-archive and aggregate ceilings
with justified headroom over these measurements. No values should be hardcoded
until these measurements are reviewed.

## Scope boundary

Stage 0 does not change supported runtime CRC values, conversion arguments, the
CLI, patch selection, PS1 conversion algorithms, APKTool, or archive behavior.
Stage 1 will harden only the custom `extractZip()` path after the measurement
gate is satisfied. APKTool resource parsing and C# defense in depth remain later
stages.
