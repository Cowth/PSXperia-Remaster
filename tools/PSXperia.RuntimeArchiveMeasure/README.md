# PSXperia Runtime Archive Measurement Utility

This standalone .NET 10 command-line tool measures local runtime APK/ZPAK
archives without extracting their entries. It is separate from the conversion
engine and does not modify selected files, the Java JAR, or production CRC
profiles. Reports are written locally as JSON and contain local input paths.

## Run interactively

```powershell
dotnet run --project .\tools\PSXperia.RuntimeArchiveMeasure
```

Enter the local APK and matching external runtime ZPAK paths. By default, the
report is written under `%LOCALAPPDATA%\PSXperiaRemastered\ArchiveMeasurements`.

## Specify paths

```powershell
dotnet run --project .\tools\PSXperia.RuntimeArchiveMeasure -- `
  --apk "D:\LocalRuntime\CrashBandicoot.apk" `
  --zpak "D:\LocalRuntime\matching-runtime.zpak" `
  --output "$env:LOCALAPPDATA\PSXperiaRemastered\ArchiveMeasurements\scan.json"
```

A network report destination is refused unless `--allow-shared-output` is
explicitly supplied.

## Synthetic sample

```powershell
dotnet run --project .\tools\PSXperia.RuntimeArchiveMeasure -- --demo
```

The demo creates placeholder APK/ZPAK-shaped archives in a private temporary
directory, reports their measurements, and removes the temporary files. It does
not use Sony runtime files, game images, or signing keys.

## Run regression checks

```powershell
dotnet run --project .\tests\PSXperia.RuntimeArchiveMeasure.Tests -c Release
```

These are self-contained synthetic regression checks; they require no external
test packages and never access a real runtime archive.

## Scanner safety budgets

Fixed ceilings are 512 MiB for APK and embedded-ZPAK compressed inputs and
551,460,579 bytes for an external-ZPAK compressed input, 100,000 entries, 1 GiB per
expanded entry, 2 GiB aggregate expansion per archive, five minutes per
measurement invocation, 2,000,000 path-analysis work items, 4,096 characters per
entry name, 10,000 findings, 32 participant-name samples per finding, and 16 MiB
of JSON report output. ZIP32 central directories are preflighted record by record
before `ZipArchive` construction, with an 8 MiB directory-byte ceiling, at most
100,000 records, and at most 16 KiB of raw filename bytes per record. The 8 MiB
ceiling is more than 1,800 times the largest central directory measured in the
supplied Crash Bandicoot reference set (4,524 bytes in its embedded ZPAK). It is
a measurement-only resource guardrail, not evidence that other Sony runtime
profiles fit it. ZIP64 and multi-disk archives remain incomplete. These scanner
budgets protect the measurement utility only; they are not conversion acceptance
limits and do not indicate whether an archive is valid or compatible. The
command-line interface cannot raise these ceilings.
The external-ZPAK ceiling is a fixed measurement-only bound equal to the observed
size of one supplied archive. It does not alter production conversion or extraction
limits; the CLI has no option to change it.

Central-directory limits were checked against the supplied Crash Bandicoot APK,
its embedded `assets/NCUA94900_1_1.zpak`, and matching external ZPAK only. The
utility does not classify CRC profiles or establish compatibility. Other supported
Sony runtime profiles have not been validated against this central-directory
ceiling; reports are structural measurements, not compatibility verdicts.

If a guard is reached, the report marks the affected measurement `incomplete`
and records the reason. It does not infer the unmeasured value. ZIP64 archives
are marked incomplete after checking the locator and ZIP64 end-of-central-
directory record, as are multi-disk archives.

If an entry name exceeds its reporting limit, that entry is shown with a
truncation marker and the remaining entries are not measured. If path-analysis
work or finding/participant limits are reached, the report records that
analysis is incomplete. If serialized JSON would exceed the report-byte limit,
the utility writes a compact report marked truncated/incomplete and exits with
status 2; it does not write a complete-status report with omitted details.

The utility reads archive entries through `System.IO.Compression.ZipArchive`.
Embedded APK ZPAKs are copied only as compressed archive payloads to a private
uniquely named temporary directory so they can be scanned as archives; no
entries are extracted. The directory is rejected if it resolves to a reparse
point, removed before the result is returned, and cleanup status is reported.
