using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PSXperia.RuntimeArchiveMeasure;

namespace PSXperia.RuntimeArchiveMeasure.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            (nameof(MeasuresThreeRolesAndFilters), MeasuresThreeRolesAndFilters),
            (nameof(ReportsPathCollisionsAndConflicts), ReportsPathCollisionsAndConflicts),
            (nameof(CentralDirectoryCeilingIsEnforced), CentralDirectoryCeilingIsEnforced),
            (nameof(CentralDirectoryOffsetAndBoundaryValidation), CentralDirectoryOffsetAndBoundaryValidation),
            (nameof(CentralDirectoryHeaderAndSignatureValidation), CentralDirectoryHeaderAndSignatureValidation),
            (nameof(CentralDirectoryFieldLengthsAreBounded), CentralDirectoryFieldLengthsAreBounded),
            (nameof(CentralDirectoryEntryCountsMustMatch), CentralDirectoryEntryCountsMustMatch),
            (nameof(CentralDirectoryFilenameLengthIsBounded), CentralDirectoryFilenameLengthIsBounded),
            (nameof(CentralDirectoryPreflightCancellationIsIncompleteAndCleaned), CentralDirectoryPreflightCancellationIsIncompleteAndCleaned),
            (nameof(BudgetHitsAreIncomplete), BudgetHitsAreIncomplete),
            (nameof(EmbeddedZpakBudgetFailurePropagates), EmbeddedZpakBudgetFailurePropagates),
            (nameof(EmbeddedZpakCancellationPropagates), EmbeddedZpakCancellationPropagates),
            (nameof(CancellationLeavesLaterEmbeddedZpaksUnmeasured), CancellationLeavesLaterEmbeddedZpaksUnmeasured),
            (nameof(ExternalZpakCompressedCeilingIsFixed), ExternalZpakCompressedCeilingIsFixed),
            (nameof(ExternalZpakCancellationStopsStreaming), ExternalZpakCancellationStopsStreaming),
            (nameof(LargeConflictArchiveUsesBoundedAnalysis), LargeConflictArchiveUsesBoundedAnalysis),
            (nameof(PathAnalysisWorkAndCancellationAreBounded), PathAnalysisWorkAndCancellationAreBounded),
            (nameof(ReportBudgetsMarkOmittedDataIncomplete), ReportBudgetsMarkOmittedDataIncomplete),
            (nameof(ReportOutputRejectsForgedLimits), ReportOutputRejectsForgedLimits),
            (nameof(DirectoryEntryPayloadIsMeasured), DirectoryEntryPayloadIsMeasured),
            (nameof(Zip64AndMultiDiskArchivesAreIncomplete), Zip64AndMultiDiskArchivesAreIncomplete),
            (nameof(MalformedInputIsIncompleteAndInputsRemainUnchanged), MalformedInputIsIncompleteAndInputsRemainUnchanged),
            (nameof(MalformedCentralDirectoryIsIncompleteAndCleaned), MalformedCentralDirectoryIsIncompleteAndCleaned),
            (nameof(CorruptCompressedEntryIsIncomplete), CorruptCompressedEntryIsIncomplete),
            (nameof(UnsafeNamesNeverWriteOutsideAndInputsStayUnchanged), UnsafeNamesNeverWriteOutsideAndInputsStayUnchanged),
            (nameof(SharedReportPathsRequireExplicitOptIn), SharedReportPathsRequireExplicitOptIn)
        };

        var failed = 0;
        foreach (var (name, run) in tests)
        {
            try { await run(); Console.WriteLine($"PASS {name}"); }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex}"); }
        }

        Console.WriteLine($"{tests.Length - failed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    private static async Task MeasuresThreeRolesAndFilters()
    {
        using var temp = new TemporaryDirectory();
        var embedded = MakeZip(("metadata.xml", "placeholder"), ("content/keep.dat", "nested"));
        var apk = Path.Combine(temp.Path, "runtime.apk");
        WriteZip(apk,
            ("AndroidManifest.xml", Bytes("placeholder")),
            ("classes.dex", Bytes("placeholder")),
            ("res/layout/view.xml", Bytes("placeholder")),
            ("assets/default.zpak", embedded),
            ("assets/keep.dat", Bytes("placeholder")));
        var zpak = Path.Combine(temp.Path, "runtime.zpak");
        WriteZip(zpak,
            ("metadata.xml", Bytes("placeholder")),
            ("data/image.ps", Bytes("placeholder")),
            ("manual/readme.txt", Bytes("placeholder")),
            ("data/keep.dat", Bytes("placeholder")));

        var report = await new ArchiveMeasurementService().MeasureAsync(apk, zpak);
        Check.Equal("complete", report.Apk.Status);
        Check.Equal("complete", report.ExternalZpak.Status);
        Check.True(report.Apk.CompressedBytes > 0);
        Check.Equal(5L, report.Apk.CentralDirectoryEntryCount);
        Check.True(report.Apk.CentralDirectoryOffset > 0);
        Check.True(report.Apk.CentralDirectoryBytes > 0);
        Check.True(report.Apk.AggregateExpandedMeasurementComplete);
        Check.Contains(report.Apk.PredictedExtractedPaths, p => p == "AndroidManifest.xml");
        Check.DoesNotContain(report.Apk.PredictedExtractedPaths, p => p == "res/layout/view.xml");
        Check.Contains(report.Apk.Entries, e => e.NormalizedPath == "res/layout/view.xml" && e.WouldExtract == false);
        Check.Equal(1, report.Apk.EmbeddedArchives.Count);
        Check.Equal("only candidate; Java will select this archive", report.Apk.EmbeddedArchives[0].SelectionStatus);
        Check.Equal("complete", report.Apk.EmbeddedArchives[0].Measurement.Status);
        Check.Contains(report.ExternalZpak.PredictedExtractedPaths, p => p == "data/keep.dat");
        Check.DoesNotContain(report.ExternalZpak.PredictedExtractedPaths, p => p == "data/image.ps");
        Check.DoesNotContain(report.ExternalZpak.PredictedExtractedPaths, p => p == "manual/readme.txt");
        Check.Equal("complete", report.TemporaryStagingCleanupStatus);
    }

    private static async Task CentralDirectoryCeilingIsEnforced()
    {
        using var temp = new TemporaryDirectory();
        var entries = Enumerable.Range(0, 2100)
            .Select(i => ($"file{i:D4}-" + new string('x', 3990), Array.Empty<byte>()))
            .ToArray();
        var oversizedZpak = Path.Combine(temp.Path, "large-central-directory.zpak");
        WriteZip(oversizedZpak, [.. entries]);
        var apk = Path.Combine(temp.Path, "valid.apk");
        WriteZip(apk, ("AndroidManifest.xml", Bytes("placeholder")));

        var report = await new ArchiveMeasurementService().MeasureAsync(apk, oversizedZpak);

        Check.Equal("incomplete", report.ExternalZpak.Status);
        Check.True(report.ExternalZpak.CentralDirectoryBytes > ScanBudget.MaxCentralDirectoryBytes);
        Check.Contains(report.ExternalZpak.IncompleteReasons,
            reason => reason.Contains("Central-directory byte length", StringComparison.Ordinal));
        Check.Equal(2100L, report.ExternalZpak.CentralDirectoryEntryCount);
    }

    private static async Task CentralDirectoryOffsetAndBoundaryValidation()
    {
        using var temp = new TemporaryDirectory();
        var apk = Path.Combine(temp.Path, "valid.apk");
        WriteZip(apk, ("AndroidManifest.xml", Bytes("placeholder")));
        var valid = MakeZipBytes(("payload.bin", Bytes("payload")));
        var eocd = FindEndRecord(valid);

        var outside = (byte[])valid.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            outside.AsSpan(eocd + 16, 4), checked((uint)outside.Length + 1));
        var outsidePath = Path.Combine(temp.Path, "offset-outside.zpak");
        await File.WriteAllBytesAsync(outsidePath, outside);

        var beyondEocd = (byte[])valid.Clone();
        var currentSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(valid.AsSpan(eocd + 12, 4));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            beyondEocd.AsSpan(eocd + 12, 4), currentSize + 1);
        var beyondEocdPath = Path.Combine(temp.Path, "directory-past-eocd.zpak");
        await File.WriteAllBytesAsync(beyondEocdPath, beyondEocd);

        var outsideReport = await new ArchiveMeasurementService().MeasureAsync(apk, outsidePath);
        var boundaryReport = await new ArchiveMeasurementService().MeasureAsync(apk, beyondEocdPath);
        Check.Equal("incomplete", outsideReport.ExternalZpak.Status);
        Check.Contains(outsideReport.ExternalZpak.IncompleteReasons,
            reason => reason.Contains("outside the archive", StringComparison.OrdinalIgnoreCase));
        Check.Equal("incomplete", boundaryReport.ExternalZpak.Status);
        Check.Contains(boundaryReport.ExternalZpak.IncompleteReasons,
            reason => reason.Contains("beyond the end-of-central-directory", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task CentralDirectoryHeaderAndSignatureValidation()
    {
        using var temp = new TemporaryDirectory();
        var apk = Path.Combine(temp.Path, "valid.apk");
        WriteZip(apk, ("AndroidManifest.xml", Bytes("placeholder")));
        var valid = MakeZipBytes(("payload.bin", Bytes("payload")));
        var eocd = FindEndRecord(valid);
        var centralOffset = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(valid.AsSpan(eocd + 16, 4)));

        var truncated = (byte[])valid.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(truncated.AsSpan(eocd + 12, 4), 45);
        var truncatedPath = Path.Combine(temp.Path, "truncated-central-header.zpak");
        await File.WriteAllBytesAsync(truncatedPath, truncated);

        var invalidSignature = (byte[])valid.Clone();
        invalidSignature[centralOffset] = 0;
        var invalidSignaturePath = Path.Combine(temp.Path, "invalid-central-signature.zpak");
        await File.WriteAllBytesAsync(invalidSignaturePath, invalidSignature);

        var truncatedReport = await new ArchiveMeasurementService().MeasureAsync(apk, truncatedPath);
        var signatureReport = await new ArchiveMeasurementService().MeasureAsync(apk, invalidSignaturePath);
        Check.Equal("incomplete", truncatedReport.ExternalZpak.Status);
        Check.Contains(truncatedReport.ExternalZpak.IncompleteReasons,
            reason => reason.Contains("truncated fixed header", StringComparison.Ordinal));
        Check.Equal("incomplete", signatureReport.ExternalZpak.Status);
        Check.Contains(signatureReport.ExternalZpak.IncompleteReasons,
            reason => reason.Contains("invalid file-header signature", StringComparison.Ordinal));
    }

    private static async Task CentralDirectoryFieldLengthsAreBounded()
    {
        using var temp = new TemporaryDirectory();
        var apk = Path.Combine(temp.Path, "valid.apk");
        WriteZip(apk, ("AndroidManifest.xml", Bytes("placeholder")));
        var valid = MakeZipBytes(("x", Bytes("payload")));
        var eocd = FindEndRecord(valid);
        var centralOffset = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(valid.AsSpan(eocd + 16, 4)));
        var fieldOffsets = new (string Name, int HeaderOffset)[]
        {
            ("filename", 28),
            ("extra-field", 30),
            ("comment", 32)
        };

        foreach (var (name, headerOffset) in fieldOffsets)
        {
            var malformed = (byte[])valid.Clone();
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                malformed.AsSpan(centralOffset + headerOffset, 2), 100);
            var path = Path.Combine(temp.Path, $"overlong-{name}.zpak");
            await File.WriteAllBytesAsync(path, malformed);
            var report = await new ArchiveMeasurementService().MeasureAsync(apk, path);
            Check.Equal("incomplete", report.ExternalZpak.Status);
            Check.Contains(report.ExternalZpak.IncompleteReasons,
                reason => reason.Contains("extends beyond the declared directory boundary", StringComparison.Ordinal));
        }
    }

    private static async Task CentralDirectoryEntryCountsMustMatch()
    {
        using var temp = new TemporaryDirectory();
        var apk = Path.Combine(temp.Path, "valid.apk");
        WriteZip(apk, ("AndroidManifest.xml", Bytes("placeholder")));

        var oneEntry = MakeZipBytes(("one.bin", Bytes("one")));
        var oneEocd = FindEndRecord(oneEntry);
        var declaredTooMany = (byte[])oneEntry.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(declaredTooMany.AsSpan(oneEocd + 8, 2), 2);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(declaredTooMany.AsSpan(oneEocd + 10, 2), 2);
        var tooManyPath = Path.Combine(temp.Path, "declared-too-many.zpak");
        await File.WriteAllBytesAsync(tooManyPath, declaredTooMany);

        var twoEntries = MakeZipBytes(("one.bin", Bytes("one")), ("two.bin", Bytes("two")));
        var twoEocd = FindEndRecord(twoEntries);
        var declaredTooFew = (byte[])twoEntries.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(declaredTooFew.AsSpan(twoEocd + 8, 2), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(declaredTooFew.AsSpan(twoEocd + 10, 2), 1);
        var tooFewPath = Path.Combine(temp.Path, "declared-too-few.zpak");
        await File.WriteAllBytesAsync(tooFewPath, declaredTooFew);

        var tooManyReport = await new ArchiveMeasurementService().MeasureAsync(apk, tooManyPath);
        var tooFewReport = await new ArchiveMeasurementService().MeasureAsync(apk, tooFewPath);
        Check.Equal("incomplete", tooManyReport.ExternalZpak.Status);
        Check.Contains(tooManyReport.ExternalZpak.IncompleteReasons,
            reason => reason.Contains("ended before the declared entry count", StringComparison.Ordinal));
        Check.Equal("incomplete", tooFewReport.ExternalZpak.Status);
        Check.Contains(tooFewReport.ExternalZpak.IncompleteReasons,
            reason => reason.Contains("does not account for its declared", StringComparison.Ordinal));
    }

    private static async Task CentralDirectoryFilenameLengthIsBounded()
    {
        using var temp = new TemporaryDirectory();
        var overlongName = new string('n', 16 * 1024 + 1);
        var zpak = Path.Combine(temp.Path, "overlong-filename.zpak");
        WriteZip(zpak, (overlongName, Array.Empty<byte>()));
        var apk = Path.Combine(temp.Path, "valid.apk");
        WriteZip(apk, ("AndroidManifest.xml", Bytes("placeholder")));

        var report = await new ArchiveMeasurementService().MeasureAsync(apk, zpak);

        Check.Equal("incomplete", report.ExternalZpak.Status);
        Check.Contains(report.ExternalZpak.IncompleteReasons,
            reason => reason.Contains("filename byte length", StringComparison.Ordinal));
    }

    private static async Task CentralDirectoryPreflightCancellationIsIncompleteAndCleaned()
    {
        using var temp = new TemporaryDirectory();
        var apk = Path.Combine(temp.Path, "valid.apk");
        var zpak = Path.Combine(temp.Path, "valid.zpak");
        WriteZip(apk, ("AndroidManifest.xml", Bytes("placeholder")));
        WriteZip(zpak, ("metadata.xml", Bytes("placeholder")));
        var stagingBefore = GetStagingDirectories();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var report = await new ArchiveMeasurementService().MeasureAsync(apk, zpak,
            cancellationToken: cancellation.Token);

        Check.Equal("incomplete", report.Apk.Status);
        Check.Equal("incomplete", report.ExternalZpak.Status);
        Check.Contains(report.Apk.IncompleteReasons,
            reason => reason.Contains("cancellation", StringComparison.OrdinalIgnoreCase));
        Check.Equal(2, MeasurementExitCode.For(report));
        Check.Equal("complete", report.TemporaryStagingCleanupStatus);
        Check.SequenceEqual(stagingBefore, GetStagingDirectories());
    }

    private static async Task ReportsPathCollisionsAndConflicts()
    {
        using var temp = new TemporaryDirectory();
        var apk = Path.Combine(temp.Path, "unsafe.apk");
        WriteZip(apk,
            ("folder\\same.txt", Bytes("a")),
            ("folder/same.txt", Bytes("b")),
            ("Name.txt", Bytes("c")),
            ("name.TXT", Bytes("d")),
            ("node", Bytes("file")),
            ("node/child", Bytes("child")),
            ("dir/", Array.Empty<byte>()),
            ("dir/", Array.Empty<byte>()),
            ("dir/child", Bytes("child")),
            ("DirCase/a", Bytes("a")),
            ("dircase/b", Bytes("b")),
            ("bad<name>.dat", Bytes("invalid windows name")),
            ("../escape", Bytes("no extraction")),
            ("C:/drive.txt", Bytes("no extraction")),
            ("\\\\server\\share\\x", Bytes("no extraction")));
        var zpak = Path.Combine(temp.Path, "empty.zpak");
        WriteZip(zpak, ("metadata.xml", Bytes("placeholder")));

        var report = await new ArchiveMeasurementService().MeasureAsync(apk, zpak);
        Check.Contains(report.Apk.PathFindings, f => f.Kind == "duplicate-normalized-path");
        Check.Contains(report.Apk.PathFindings, f => f.Kind == "case-insensitive-collision");
        Check.Contains(report.Apk.PathFindings, f => f.Kind == "file-directory-conflict" && f.Path == "node");
        Check.Contains(report.Apk.PathFindings, f => f.Kind == "explicit-and-implicit-directory" && f.Path == "dir");
        Check.Contains(report.Apk.PathFindings, f => f.Kind == "duplicate-explicit-directory" && f.Path == "dir");
        Check.Contains(report.Apk.PathFindings, f => f.Kind == "case-insensitive-collision" && f.Path == "DirCase");
        Check.Contains(report.Apk.PathFindings, f => f.Kind == "traversal-path");
        Check.Contains(report.Apk.PathFindings, f => f.Kind == "unsafe-path");
        Check.Contains(report.Apk.PathFindings, f => f.Kind == "windows-ambiguous-path" && f.Path == "bad<name>.dat");
    }

    private static async Task BudgetHitsAreIncomplete()
    {
        using var temp = new TemporaryDirectory();
        var apk = Path.Combine(temp.Path, "many.apk");
        WriteZip(apk, ("one", Bytes("1")), ("two", Bytes("2")), ("three", Bytes("3")));
        var zpak = Path.Combine(temp.Path, "large.zpak");
        WriteZip(zpak, ("large.bin", new byte[128 * 1024]));

        var countReport = await new ArchiveMeasurementService().MeasureAsync(apk, zpak,
            ScanBudget.Default with { MaxEntryCount = 2 });
        var expandedReport = await new ArchiveMeasurementService().MeasureAsync(apk, zpak,
            ScanBudget.Default with { MaxEntryExpandedBytes = 1024, MaxAggregateExpandedBytes = 4096 });
        Check.Equal("incomplete", countReport.Apk.Status);
        Check.Contains(countReport.Apk.IncompleteReasons, r => r.Contains("entry count", StringComparison.OrdinalIgnoreCase));
        Check.Equal("incomplete", expandedReport.ExternalZpak.Status);
        Check.Contains(expandedReport.ExternalZpak.IncompleteReasons, r => r.Contains("expanded bytes", StringComparison.OrdinalIgnoreCase));
        Check.Equal("incomplete-safety-budget", expandedReport.ExternalZpak.Entries.Single().MeasurementStatus);
        Check.True(expandedReport.ExternalZpak.Entries.Single().ActualExpandedBytes > 0);
    }

    private static async Task EmbeddedZpakBudgetFailurePropagates()
    {
        using var temp = new TemporaryDirectory();
        var embedded = MakeZipBytes(("payload.bin", new byte[256 * 1024]));
        var apk = Path.Combine(temp.Path, "runtime.apk");
        WriteZip(apk, ("assets/default.zpak", embedded));
        var external = Path.Combine(temp.Path, "runtime.zpak");
        WriteZip(external, ("metadata.xml", Bytes("placeholder")));

        var report = await new ArchiveMeasurementService().MeasureAsync(apk, external,
            ScanBudget.Default with { MaxEntryExpandedBytes = 8 * 1024, MaxAggregateExpandedBytes = 1024 * 1024 });
        Check.Equal("incomplete", report.Apk.EmbeddedArchives.Single().Measurement.Status);
        Check.Equal("incomplete", report.Apk.Status);
        Check.Contains(report.Apk.IncompleteReasons, r => r.Contains("Embedded ZPAK", StringComparison.Ordinal));
        Check.True(!report.IsComplete);
        Check.Equal(2, MeasurementExitCode.For(report));

        var malformedApk = Path.Combine(temp.Path, "malformed-embedded.apk");
        WriteZip(malformedApk, ("assets/default.zpak", Bytes("not a ZIP archive")));
        var malformed = await new ArchiveMeasurementService().MeasureAsync(malformedApk, external);
        Check.Equal("incomplete", malformed.Apk.EmbeddedArchives.Single().Measurement.Status);
        Check.Equal("incomplete", malformed.Apk.Status);
        Check.Equal(2, MeasurementExitCode.For(malformed));
    }

    private static async Task EmbeddedZpakCancellationPropagates()
    {
        using var temp = new TemporaryDirectory();
        var embedded = MakeZipBytes(("payload.bin", Bytes("small synthetic payload")));
        var apk = Path.Combine(temp.Path, "runtime.apk");
        WriteZip(apk, ("assets/default.zpak", embedded));
        var external = Path.Combine(temp.Path, "runtime.zpak");
        WriteZip(external, ("metadata.xml", Bytes("placeholder")));
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(value =>
        {
            if (value == "path-analysis-start:EmbeddedDefaultZpak")
                cancellation.Cancel();
        });

        var report = await new ArchiveMeasurementService().MeasureAsync(apk, external,
            cancellationToken: cancellation.Token, progress: progress);
        Check.Equal("incomplete", report.Apk.EmbeddedArchives.Single().Measurement.Status);
        Check.Equal("incomplete", report.Apk.Status);
        Check.True(!report.IsComplete);
    }

    private static async Task CancellationLeavesLaterEmbeddedZpaksUnmeasured()
    {
        using var temp = new TemporaryDirectory();
        var first = MakeZipBytes(("payload.bin", RepeatedBytes(1024 * 1024, 0x41)));
        var second = MakeZipBytes(("payload.bin", RepeatedBytes(1024 * 1024, 0x42)));
        var apk = Path.Combine(temp.Path, "runtime-multiple-embedded.apk");
        WriteZip(apk,
            ("assets/first.zpak", first),
            ("assets/second.zpak", second));
        var external = Path.Combine(temp.Path, "runtime.zpak");
        WriteZip(external, ("metadata.xml", Bytes("placeholder")));

        using var cancellation = new CancellationTokenSource();
        var progressEvents = new List<string>();
        var stagingBefore = GetStagingDirectories();
        long bytesConsumedBeforeCancellation = 0;
        var progress = new InlineProgress(value =>
        {
            progressEvents.Add(value);
            const string chunkPrefix = "entry-read-chunk:EmbeddedDefaultZpak:payload.bin:";
            if (value.StartsWith(chunkPrefix, StringComparison.Ordinal))
            {
                bytesConsumedBeforeCancellation = long.Parse(value[chunkPrefix.Length..]);
                cancellation.Cancel();
            }
        });

        var report = await new ArchiveMeasurementService().MeasureAsync(apk, external,
            cancellationToken: cancellation.Token, progress: progress);

        Check.Equal(2, report.Apk.EmbeddedArchives.Count);
        Check.Equal("incomplete", report.Apk.EmbeddedArchives[0].Measurement.Status);
        Check.Equal("unmeasured", report.Apk.EmbeddedArchives[1].Measurement.Status);
        Check.Contains(report.Apk.EmbeddedArchives[1].Measurement.IncompleteReasons,
            reason => reason.Contains("Not measured", StringComparison.OrdinalIgnoreCase));
        Check.DoesNotContain(progressEvents, "embedded-start:assets/second.zpak");
        Check.True(bytesConsumedBeforeCancellation >= 16 * 1024,
            "Cancellation must happen only after an actual nested entry read chunk was consumed.");
        Check.Contains(progressEvents, value => value.StartsWith(
            "embedded-start:assets/first.zpak", StringComparison.Ordinal));
        Check.Contains(report.Apk.IncompleteReasons,
            reason => reason.Contains("was not measured", StringComparison.OrdinalIgnoreCase));
        Check.Equal("incomplete", report.Apk.Status);
        Check.True(!report.IsComplete);
        Check.Equal(2, MeasurementExitCode.For(report));
        Check.Equal("complete", report.TemporaryStagingCleanupStatus);
        Check.SequenceEqual(stagingBefore, GetStagingDirectories());
    }

    private static async Task ExternalZpakCompressedCeilingIsFixed()
    {
        var budget = ScanBudget.Default;
        Check.Equal(551_460_579L, ScanBudget.ExternalZpakCompressedBytesCeiling);
        Check.Equal(551_460_579L, budget.CompressedArchiveLimitFor(ExtractionRole.ExternalRuntimeZpak));
        Check.Equal(512L * 1024 * 1024, budget.CompressedArchiveLimitFor(ExtractionRole.RuntimeApk));
        Check.Equal(512L * 1024 * 1024, budget.CompressedArchiveLimitFor(ExtractionRole.EmbeddedDefaultZpak));
        Check.True(budget.AllowsCompressedArchive(ExtractionRole.ExternalRuntimeZpak, 551_460_579L),
            "The exact fixed external-ZPAK boundary must be accepted.");
        Check.True(!budget.AllowsCompressedArchive(ExtractionRole.ExternalRuntimeZpak, 551_460_580L),
            "One byte above the fixed boundary must be rejected.");

        using var temp = new TemporaryDirectory();
        var archive = Path.Combine(temp.Path, "small-synthetic.zpak");
        WriteZip(archive, ("metadata.xml", Bytes("synthetic")));
        var apk = Path.Combine(temp.Path, "synthetic.apk");
        WriteZip(apk, ("AndroidManifest.xml", Bytes("placeholder")));
        var cannotRaiseLimit = false;
        try
        {
            await new ArchiveMeasurementService().MeasureAsync(apk, archive,
                budget with { MaxExternalZpakBytes = ScanBudget.ExternalZpakCompressedBytesCeiling + 1 });
        }
        catch (ArgumentOutOfRangeException) { cannotRaiseLimit = true; }
        Check.True(cannotRaiseLimit, "A caller must not raise the fixed measurement ceiling.");
    }

    private static async Task ExternalZpakCancellationStopsStreaming()
    {
        using var temp = new TemporaryDirectory();
        var apk = Path.Combine(temp.Path, "synthetic.apk");
        WriteZip(apk, ("AndroidManifest.xml", Bytes("placeholder")));
        var zpak = Path.Combine(temp.Path, "synthetic.zpak");
        WriteZip(zpak, ("payload.bin", new byte[1024 * 1024]));

        using var cancellation = new CancellationTokenSource();
        var stagingBefore = GetStagingDirectories();
        long bytesConsumedBeforeCancellation = 0;
        var progress = new InlineProgress(value =>
        {
            const string chunkPrefix = "entry-read-chunk:ExternalRuntimeZpak:payload.bin:";
            if (value.StartsWith(chunkPrefix, StringComparison.Ordinal))
            {
                bytesConsumedBeforeCancellation = long.Parse(value[chunkPrefix.Length..]);
                cancellation.Cancel();
            }
        });
        var report = await new ArchiveMeasurementService().MeasureAsync(apk, zpak,
            cancellationToken: cancellation.Token, progress: progress);

        Check.Equal("complete", report.Apk.Status);
        Check.Equal("incomplete", report.ExternalZpak.Status);
        Check.True(bytesConsumedBeforeCancellation >= 16 * 1024,
            "Cancellation must happen only after an actual external entry read chunk was consumed.");
        Check.Contains(report.ExternalZpak.IncompleteReasons,
            reason => reason.Contains("cancellation", StringComparison.OrdinalIgnoreCase));
        Check.Equal(0, report.ExternalZpak.Entries.Count);
        Check.True(!report.IsComplete);
        Check.Equal(2, MeasurementExitCode.For(report));
        Check.Equal("complete", report.TemporaryStagingCleanupStatus);
        Check.SequenceEqual(stagingBefore, GetStagingDirectories());
    }

    private static async Task LargeConflictArchiveUsesBoundedAnalysis()
    {
        using var temp = new TemporaryDirectory();
        var archive = Path.Combine(temp.Path, "conflicts.apk");
        var entries = new List<(string Name, byte[] Content)>();
        for (var i = 0; i < 5000; i++)
        {
            var name = $"node{i:D5}";
            entries.Add((name, Bytes("file")));
            entries.Add(($"{name}/child", Bytes("child")));
        }
        WriteZip(archive, [.. entries]);
        var external = Path.Combine(temp.Path, "runtime.zpak");
        WriteZip(external, ("metadata.xml", Bytes("placeholder")));

        var report = await new ArchiveMeasurementService().MeasureAsync(archive, external,
            ScanBudget.Default with { MaxPathAnalysisWorkItems = 500_000 });
        Check.Equal("complete", report.Apk.Status);
        Check.Equal(5000, report.Apk.PathFindings.Count(f => f.Kind == "file-directory-conflict"));
        Check.True(report.ScanSafetyBudget.MaxPathAnalysisWorkItems >= 500_000);
    }

    private static async Task PathAnalysisWorkAndCancellationAreBounded()
    {
        using var temp = new TemporaryDirectory();
        var archive = Path.Combine(temp.Path, "many-conflicts.apk");
        WriteZip(archive, ("alpha", Bytes("file")), ("alpha/child", Bytes("child")));
        var external = Path.Combine(temp.Path, "runtime.zpak");
        WriteZip(external, ("metadata.xml", Bytes("placeholder")));

        var workLimited = await new ArchiveMeasurementService().MeasureAsync(archive, external,
            ScanBudget.Default with { MaxPathAnalysisWorkItems = 2 });
        Check.Equal("incomplete", workLimited.Apk.Status);
        Check.Contains(workLimited.Apk.IncompleteReasons, r => r.Contains("Path analysis reached", StringComparison.Ordinal));

        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(value =>
        {
            if (value == "path-analysis-start:RuntimeApk") cancellation.Cancel();
        });
        var cancelled = await new ArchiveMeasurementService().MeasureAsync(archive, external,
            cancellationToken: cancellation.Token, progress: progress);
        Check.Equal("incomplete", cancelled.Apk.Status);
        Check.Contains(cancelled.Apk.IncompleteReasons, r => r.Contains("Path analysis stopped", StringComparison.Ordinal));
        Check.Equal(2, MeasurementExitCode.For(cancelled));
    }

    private static async Task ReportBudgetsMarkOmittedDataIncomplete()
    {
        using var temp = new TemporaryDirectory();
        var archive = Path.Combine(temp.Path, "many-entries.apk");
        var entries = Enumerable.Range(0, 100)
            .Select(i => ($"entry-{i:D3}-" + new string('n', 80), Bytes("value")))
            .ToArray();
        WriteZip(archive, entries);
        var external = Path.Combine(temp.Path, "runtime.zpak");
        WriteZip(external, ("metadata.xml", Bytes("placeholder")));

        var reportBudget = ScanBudget.Default with { MaxReportBytes = 4096 };
        var report = await new ArchiveMeasurementService().MeasureAsync(archive, external, reportBudget);
        var bounded = ReportOutput.ToBoundedJson(report);
        Check.True(bounded.Report.ReportTruncated);
        Check.True(!bounded.Report.IsComplete);
        Check.Equal(2, MeasurementExitCode.For(bounded.Report));
        Check.True(System.Text.Encoding.UTF8.GetByteCount(bounded.Json) <= reportBudget.MaxReportBytes);
        Check.Empty(bounded.Report.Apk.Entries);
        Check.Contains(bounded.Report.ReportIncompleteReasons!, r => r.Contains("report-size limit", StringComparison.Ordinal));
        var savedReportPath = Path.Combine(temp.Path, "bounded-report.json");
        var savedReport = await ReportOutput.SaveJsonAsync(report, savedReportPath, allowSharedOutput: false);
        Check.True(savedReport.ReportTruncated);
        Check.True(new FileInfo(savedReportPath).Length <= reportBudget.MaxReportBytes);
        Check.Empty(Directory.EnumerateFiles(temp.Path, "*.tmp"));

        var nameLimited = await new ArchiveMeasurementService().MeasureAsync(archive, external,
            ScanBudget.Default with { MaxEntryNameCharacters = 32 });
        Check.Equal("incomplete", nameLimited.Apk.Status);
        Check.Equal("incomplete-report-budget", nameLimited.Apk.Entries.Single().MeasurementStatus);
        Check.True(nameLimited.Apk.Entries.Single().OriginalName.EndsWith("[truncated]", StringComparison.Ordinal));

        var findingLimitedArchive = Path.Combine(temp.Path, "many-findings.apk");
        WriteZip(findingLimitedArchive, ("../one", Bytes("a")), ("../two", Bytes("b")), ("../three", Bytes("c")));
        var findingLimited = await new ArchiveMeasurementService().MeasureAsync(findingLimitedArchive, external,
            ScanBudget.Default with { MaxFindingCount = 2 });
        Check.Equal("incomplete", findingLimited.Apk.Status);
        Check.Equal(2, findingLimited.Apk.PathFindings.Count);
        Check.Contains(findingLimited.Apk.IncompleteReasons, r => r.Contains("finding count", StringComparison.OrdinalIgnoreCase));

        var rejectedLargerBudget = false;
        try
        {
            await new ArchiveMeasurementService().MeasureAsync(archive, external,
                ScanBudget.Default with { MaxReportBytes = ScanBudget.Default.MaxReportBytes + 1 });
        }
        catch (ArgumentOutOfRangeException) { rejectedLargerBudget = true; }
        Check.True(rejectedLargerBudget, "Scanner safety ceilings must not be raised by a caller.");
    }

    private static async Task ReportOutputRejectsForgedLimits()
    {
        using var temp = new TemporaryDirectory();
        var apk = Path.Combine(temp.Path, "small.apk");
        var zpak = Path.Combine(temp.Path, "small.zpak");
        WriteZip(apk, ("AndroidManifest.xml", Bytes("synthetic placeholder")));
        WriteZip(zpak, ("metadata.xml", Bytes("synthetic placeholder")));

        var normal = await new ArchiveMeasurementService().MeasureAsync(apk, zpak);
        var bounded = ReportOutput.ToBoundedJson(normal);
        Check.Equal("complete", bounded.Report.Apk.Status);
        Check.True(!bounded.Report.ReportTruncated);
        Check.True(Encoding.UTF8.GetByteCount(bounded.Json) <= 16L * 1024 * 1024);

        var savedPath = Path.Combine(temp.Path, "normal-report.json");
        var saved = await ReportOutput.SaveJsonAsync(normal, savedPath, allowSharedOutput: false);
        Check.Equal("complete", saved.Apk.Status);
        Check.True(new FileInfo(savedPath).Length <= 16L * 1024 * 1024);

        var forged = normal with
        {
            ScanSafetyBudget = normal.ScanSafetyBudget with { MaxReportBytes = 16L * 1024 * 1024 + 1 }
        };
        var toBoundedRejected = false;
        try { _ = ReportOutput.ToBoundedJson(forged); }
        catch (ArgumentOutOfRangeException) { toBoundedRejected = true; }
        Check.True(toBoundedRejected, "ToBoundedJson must reject a forged report budget above the fixed ceiling.");

        var forgedSavePath = Path.Combine(temp.Path, "forged-report.json");
        var saveRejected = false;
        try { _ = await ReportOutput.SaveJsonAsync(forged, forgedSavePath, allowSharedOutput: false); }
        catch (ArgumentOutOfRangeException) { saveRejected = true; }
        Check.True(saveRejected, "SaveJsonAsync must enforce the same fixed report ceiling.");
        Check.True(!File.Exists(forgedSavePath), "Rejected serialization must not create an output file.");

        var tooSmall = normal with
        {
            ScanSafetyBudget = normal.ScanSafetyBudget with { MaxReportBytes = 4095 }
        };
        var smallRejected = false;
        try { _ = ReportOutput.ToBoundedJson(tooSmall); }
        catch (ArgumentOutOfRangeException) { smallRejected = true; }
        Check.True(smallRejected, "Invalid report budgets below the supported minimum must be rejected.");
    }

    private static async Task DirectoryEntryPayloadIsMeasured()
    {
        using var temp = new TemporaryDirectory();
        var apk = Path.Combine(temp.Path, "directory-payload.apk");
        WriteZip(apk, ("directory/", Bytes("nonzero directory payload")));
        var external = Path.Combine(temp.Path, "runtime.zpak");
        WriteZip(external, ("metadata.xml", Bytes("placeholder")));

        var report = await new ArchiveMeasurementService().MeasureAsync(apk, external);
        var entry = report.Apk.Entries.Single();
        Check.Equal("complete-directory-entry", entry.MeasurementStatus);
        Check.Equal((long)Bytes("nonzero directory payload").Length, entry.ActualExpandedBytes);
        Check.Equal(entry.ActualExpandedBytes, report.Apk.MeasuredExpandedBytes);
    }

    private static async Task Zip64AndMultiDiskArchivesAreIncomplete()
    {
        using var temp = new TemporaryDirectory();
        var normal = MakeZipBytes(("metadata.xml", Bytes("placeholder")));
        var zip64Path = Path.Combine(temp.Path, "zip64.zpak");
        await File.WriteAllBytesAsync(zip64Path, InsertZip64Locator(normal));
        var multidisk = (byte[])normal.Clone();
        var eocd = FindEndRecord(multidisk);
        multidisk[eocd + 4] = 1;
        var multiPath = Path.Combine(temp.Path, "multidisk.zpak");
        await File.WriteAllBytesAsync(multiPath, multidisk);
        var apk = Path.Combine(temp.Path, "runtime.apk");
        WriteZip(apk, ("AndroidManifest.xml", Bytes("placeholder")));

        var zip64Report = await new ArchiveMeasurementService().MeasureAsync(apk, zip64Path);
        var multiReport = await new ArchiveMeasurementService().MeasureAsync(apk, multiPath);
        Check.Equal("incomplete", zip64Report.ExternalZpak.Status);
        Check.Equal(1L, zip64Report.ExternalZpak.CentralDirectoryEntryCount);
        Check.Contains(zip64Report.ExternalZpak.IncompleteReasons, r => r.Contains("ZIP64", StringComparison.OrdinalIgnoreCase));
        Check.Equal("incomplete", multiReport.ExternalZpak.Status);
        Check.Contains(multiReport.ExternalZpak.IncompleteReasons, r => r.Contains("Multi-disk", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task MalformedInputIsIncompleteAndInputsRemainUnchanged()
    {
        using var temp = new TemporaryDirectory();
        var bad = Path.Combine(temp.Path, "bad.apk");
        await File.WriteAllBytesAsync(bad, Bytes("not a zip"));
        var zpak = Path.Combine(temp.Path, "ok.zpak");
        WriteZip(zpak, ("data/image.ps", Bytes("placeholder")));
        var beforeApk = SHA256.HashData(await File.ReadAllBytesAsync(bad));
        var beforeZpak = SHA256.HashData(await File.ReadAllBytesAsync(zpak));

        var report = await new ArchiveMeasurementService().MeasureAsync(bad, zpak);
        Check.Equal("incomplete", report.Apk.Status);
        Check.Equal("complete", report.ExternalZpak.Status);
        Check.SequenceEqual(beforeApk, SHA256.HashData(await File.ReadAllBytesAsync(bad)));
        Check.SequenceEqual(beforeZpak, SHA256.HashData(await File.ReadAllBytesAsync(zpak)));
        Check.Equal("complete", report.TemporaryStagingCleanupStatus);
    }

    private static async Task MalformedCentralDirectoryIsIncompleteAndCleaned()
    {
        using var temp = new TemporaryDirectory();
        var malformedNested = MakeZipBytes(("payload.bin", Bytes("synthetic nested payload")));
        var centralDirectory = FindSignature(malformedNested, 0x02014B50);
        malformedNested[centralDirectory] = 0;

        var apk = Path.Combine(temp.Path, "malformed-nested.apk");
        WriteZip(apk, ("assets/default.zpak", malformedNested));
        var external = Path.Combine(temp.Path, "valid.zpak");
        WriteZip(external, ("metadata.xml", Bytes("placeholder")));
        var stagingBefore = GetStagingDirectories();
        var apkBefore = SHA256.HashData(await File.ReadAllBytesAsync(apk));

        var report = await new ArchiveMeasurementService().MeasureAsync(apk, external);

        Check.Equal("incomplete", report.Apk.Status);
        Check.Equal("incomplete", report.Apk.EmbeddedArchives.Single().Measurement.Status);
        Check.Contains(report.Apk.EmbeddedArchives.Single().Measurement.IncompleteReasons,
            reason => reason.Contains("invalid", StringComparison.OrdinalIgnoreCase));
        Check.Equal(2, MeasurementExitCode.For(report));
        Check.Equal("complete", report.TemporaryStagingCleanupStatus);
        Check.SequenceEqual(stagingBefore, GetStagingDirectories());
        Check.SequenceEqual(apkBefore, SHA256.HashData(await File.ReadAllBytesAsync(apk)));
    }

    private static async Task CorruptCompressedEntryIsIncomplete()
    {
        using var temp = new TemporaryDirectory();
        var corrupt = MakeDeflatedZipBytes("payload.bin", RepeatedBytes(128 * 1024, 0x5A));
        CorruptFirstDeflateBlock(corrupt);
        var zpak = Path.Combine(temp.Path, "corrupt.zpak");
        await File.WriteAllBytesAsync(zpak, corrupt);
        var apk = Path.Combine(temp.Path, "valid.apk");
        WriteZip(apk, ("AndroidManifest.xml", Bytes("placeholder")));
        var before = SHA256.HashData(await File.ReadAllBytesAsync(zpak));

        var report = await new ArchiveMeasurementService().MeasureAsync(apk, zpak);

        Check.Equal("incomplete", report.ExternalZpak.Status);
        Check.Contains(report.ExternalZpak.Entries, entry => entry.MeasurementStatus == "incomplete-error");
        Check.Contains(report.ExternalZpak.IncompleteReasons,
            reason => reason.Contains("InvalidDataException", StringComparison.Ordinal));
        Check.Equal(2, MeasurementExitCode.For(report));
        Check.SequenceEqual(before, SHA256.HashData(await File.ReadAllBytesAsync(zpak)));
        Check.Equal("complete", report.TemporaryStagingCleanupStatus);
    }

    private static async Task UnsafeNamesNeverWriteOutsideAndInputsStayUnchanged()
    {
        using var temp = new TemporaryDirectory();
        var outside = Path.Combine(temp.Path, "outside.txt");
        await File.WriteAllTextAsync(outside, "sentinel");
        var apk = Path.Combine(temp.Path, "malicious.apk");
        WriteZip(apk, ("../../outside.txt", Bytes("attack")), ("D:/outside", Bytes("attack")));
        var zpak = Path.Combine(temp.Path, "zpak.zip");
        WriteZip(zpak, ("metadata.xml", Bytes("placeholder")));
        var beforeApk = SHA256.HashData(await File.ReadAllBytesAsync(apk));
        var beforeZpak = SHA256.HashData(await File.ReadAllBytesAsync(zpak));

        var report = await new ArchiveMeasurementService().MeasureAsync(apk, zpak);
        Check.Contains(report.Apk.PathFindings, f => f.Kind == "traversal-path");
        Check.Equal("sentinel", await File.ReadAllTextAsync(outside));
        Check.SequenceEqual(beforeApk, SHA256.HashData(await File.ReadAllBytesAsync(apk)));
        Check.SequenceEqual(beforeZpak, SHA256.HashData(await File.ReadAllBytesAsync(zpak)));
    }

    private static Task SharedReportPathsRequireExplicitOptIn()
    {
        Check.True(ReportOutput.IsRemotePath("\\\\server\\share\\scan.json"));
        Check.True(!ReportOutput.IsRemotePath(Path.Combine(Path.GetTempPath(), "scan.json")));
        Check.True(!ReportOutput.HasExistingReparseComponent(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "scan.json")));
        return Task.CompletedTask;
    }

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    private static byte[] RepeatedBytes(int count, byte value)
    {
        var data = new byte[count];
        Array.Fill(data, value);
        return data;
    }

    private static string[] GetStagingDirectories() => Directory.GetDirectories(
        System.IO.Path.GetTempPath(), "PSXperiaArchiveMeasure-*", SearchOption.TopDirectoryOnly)
        .Order(StringComparer.Ordinal).ToArray();

    private static byte[] MakeDeflatedZipBytes(string name, byte[] content)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        using (var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open())
            entry.Write(content);
        return memory.ToArray();
    }

    private static void CorruptFirstDeflateBlock(byte[] archive)
    {
        var localHeader = FindSignature(archive, 0x04034B50);
        var method = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(localHeader + 8, 2));
        Check.Equal((ushort)8, method);
        var nameLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(localHeader + 26, 2));
        var extraLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(localHeader + 28, 2));
        var dataOffset = checked(localHeader + 30 + nameLength + extraLength);
        archive[dataOffset] = (byte)((archive[dataOffset] & 0xF8) | 0x06); // DEFLATE BTYPE=3 is reserved/invalid.
    }

    private static int FindSignature(byte[] data, uint signature)
    {
        for (var i = 0; i <= data.Length - 4; i++)
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i, 4)) == signature) return i;
        throw new InvalidOperationException($"Synthetic ZIP signature 0x{signature:X8} was not found.");
    }
    private static byte[] MakeZipBytes(params (string Name, byte[] Content)[] entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
                using (var stream = zip.CreateEntry(name).Open()) stream.Write(content);
        return memory.ToArray();
    }

    private static byte[] InsertZip64Locator(byte[] archive)
    {
        var eocd = FindEndRecord(archive);
        var result = new byte[archive.Length + 76];
        Array.Copy(archive, 0, result, 0, eocd);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(eocd, 4), 0x06064B50);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(eocd + 4, 8), 44);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(eocd + 12, 2), 45);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(eocd + 14, 2), 45);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(eocd + 24, 8), System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(eocd + 8, 2)));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(eocd + 32, 8), System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(eocd + 10, 2)));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(eocd + 40, 8), System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(eocd + 12, 4)));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(eocd + 48, 8), System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(eocd + 16, 4)));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(eocd + 56, 4), 0x07064B50);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(eocd + 64, 8), (ulong)eocd);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(eocd + 72, 4), 1);
        Array.Copy(archive, eocd, result, eocd + 76, archive.Length - eocd);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(eocd + 76 + 8, 2), ushort.MaxValue);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(eocd + 76 + 10, 2), ushort.MaxValue);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(eocd + 76 + 12, 4), uint.MaxValue);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(eocd + 76 + 16, 4), uint.MaxValue);
        return result;
    }

    private static int FindEndRecord(byte[] data)
    {
        for (var i = data.Length - 22; i >= 0; i--)
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i, 4)) == 0x06054B50) return i;
        throw new InvalidOperationException("Synthetic ZIP had no end record.");
    }

    private static byte[] MakeZip(params (string Name, string Content)[] entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(content);
            }
        return memory.ToArray();
    }

    private static void WriteZip(string path, params (string Name, byte[] Content)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
            using (var stream = zip.CreateEntry(name).Open()) stream.Write(content);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { Directory.Delete(Path, recursive: true); }
    }
}

internal sealed class InlineProgress(Action<string> callback) : IProgress<string>
{
    public void Report(string value) => callback(value);
}

internal static class Check
{
    public static void True(bool value, string? message = null)
    { if (!value) throw new InvalidOperationException(message ?? "Expected true."); }
    public static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected '{expected}', got '{actual}'."); }
    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    { if (!expected.SequenceEqual(actual)) throw new InvalidOperationException("Sequences differ."); }
    public static void Contains<T>(IEnumerable<T> values, Func<T, bool> predicate)
    { if (!values.Any(predicate)) throw new InvalidOperationException("Expected sequence item was not found."); }
    public static void DoesNotContain<T>(IEnumerable<T> values, Func<T, bool> predicate)
    { if (values.Any(predicate)) throw new InvalidOperationException("Unexpected sequence item was found."); }
    public static void DoesNotContain<T>(IEnumerable<T> values, T value)
    { if (values.Contains(value)) throw new InvalidOperationException($"Unexpected item '{value}'."); }
    public static void Empty<T>(IEnumerable<T> values)
    { if (values.Any()) throw new InvalidOperationException("Expected an empty sequence."); }
}
