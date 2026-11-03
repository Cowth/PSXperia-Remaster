using System.IO.Compression;
using System.Text;

namespace PSXperia.RuntimeArchiveMeasure;

public sealed class ArchiveMeasurementService
{
    public async Task<MeasurementReport> MeasureAsync(
        string apkPath,
        string externalZpakPath,
        ScanBudget? budget = null,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        budget ??= ScanBudget.Default;
        ValidateBudget(budget);

        var stagingRoot = Path.Combine(Path.GetTempPath(), $"PSXperiaArchiveMeasure-{Guid.NewGuid():N}");
        MeasurementReport? report = null;
        var cleanupStatus = "complete";
        var pathWorkBudget = new PathAnalysisWorkBudget(budget.MaxPathAnalysisWorkItems);
        try
        {
            Directory.CreateDirectory(stagingRoot);
            if ((File.GetAttributes(stagingRoot) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The private temporary staging directory resolved to a reparse point.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(budget.MaxDuration);
            var apk = await MeasureArchiveAsync(apkPath, ExtractionRole.RuntimeApk, budget, stagingRoot, pathWorkBudget, timeout.Token, progress: progress);
            var external = await MeasureArchiveAsync(externalZpakPath, ExtractionRole.ExternalRuntimeZpak, budget, stagingRoot, pathWorkBudget, timeout.Token, progress: progress);
            report = new(
                UtilityVersion: "1.0.0",
                MeasuredAtUtc: DateTimeOffset.UtcNow,
                Runtime: System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                ScanSafetyBudget: budget,
                ScopeNote: "Read-only measurement. No archive entries were extracted. Predicted paths mirror the existing Java filters for a fresh output directory. Scanner safety budgets are operational caps, not production archive limits or compatibility claims. The central-directory ceiling was calibrated against the supplied Crash Bandicoot APK, embedded ZPAK, and external ZPAK only; other Sony runtime profiles are unverified, and this utility does not classify CRC profiles.",
                TemporaryStagingCleanupStatus: "pending",
                Apk: apk,
                ExternalZpak: external);
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingRoot))
                {
                    var attributes = File.GetAttributes(stagingRoot);
                    Directory.Delete(stagingRoot, recursive: (attributes & FileAttributes.ReparsePoint) == 0);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { cleanupStatus = $"incomplete: temporary nested-archive staging could not be removed: {ex.Message}"; }
        }
        return report! with { TemporaryStagingCleanupStatus = cleanupStatus };
    }

    private static async Task<ArchiveMeasurementReport> MeasureArchiveAsync(
        string path,
        ExtractionRole role,
        ScanBudget budget,
        string stagingRoot,
        PathAnalysisWorkBudget pathWorkBudget,
        CancellationToken cancellationToken,
        string? displayPath = null,
        IProgress<string>? progress = null)
    {
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return Incomplete(path, role, $"Input path could not be resolved: {ex.Message}"); }
        var reportPath = displayPath ?? fullPath;
        var notes = new List<string>();
        var incomplete = new List<string>();
        var entries = new List<EntryMeasurement>();
        var findings = new List<PathFinding>();
        var predicted = new List<string>();
        var embedded = new List<EmbeddedArchiveMeasurement>();
        long? compressedBytes = null;
        long? centralCount = null;
        long? centralDirectoryOffset = null;
        long? centralDirectoryBytes = null;
        long? declaredAggregate = null;
        long measuredAggregate = 0;

        if (!File.Exists(fullPath))
            return Incomplete(reportPath, role, "Input file does not exist.");

        var embeddedSpools = new List<(string EntryName, string SpoolPath)>();
        try
        {
            await using var input = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            compressedBytes = input.Length;
            var compressedLimit = budget.CompressedArchiveLimitFor(role);
            if (!budget.AllowsCompressedArchive(role, compressedBytes.Value))
                return Incomplete(reportPath, role, $"Archive length {compressedBytes} exceeds scanner safety budget {compressedLimit}.", compressedBytes);

            ZipProbeResult probe;
            try
            {
                probe = ZipDirectoryProbe.Probe(input, input.Length, budget.MaxEntryCount,
                    budget.MaxEntryNameCharacters, ScanBudget.MaxCentralDirectoryBytes, cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            { return Incomplete(reportPath, role, $"ZIP preflight could not complete: {BoundedDiagnostic(ex)}", compressedBytes); }

            centralCount = probe.EntryCount;
            centralDirectoryOffset = probe.CentralDirectoryOffset;
            centralDirectoryBytes = probe.CentralDirectoryBytes;
            if (!probe.Complete)
                return Incomplete(reportPath, role, probe.Reason ?? "ZIP preflight was incomplete.", compressedBytes,
                    centralCount, probe.CentralDirectoryOffset, probe.CentralDirectoryBytes);
            input.Position = 0;
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count != centralCount)
                return Incomplete(reportPath, role,
                    $"ZIP entry count changed between bounded preflight ({centralCount}) and archive reader ({archive.Entries.Count}).",
                    compressedBytes, archive.Entries.Count, probe.CentralDirectoryOffset, probe.CentralDirectoryBytes);

            var nestedCandidates = role == ExtractionRole.RuntimeApk
                ? archive.Entries.Where(IsTopLevelAssetsZpak).ToArray()
                : [];
            if (role == ExtractionRole.RuntimeApk && nestedCandidates.Length == 0)
                notes.Add("No top-level assets/*.zpak candidate was found for the Java embedded-ZPAK extraction step.");
            if (nestedCandidates.Length > 1)
                notes.Add("Multiple top-level assets/*.zpak candidates exist. Java selects the first filesystem enumeration result; that exact choice is not knowable from a read-only archive report. All candidates are measured separately.");

            var spoolByEntry = new Dictionary<ZipArchiveEntry, string>();
            for (var i = 0; i < nestedCandidates.Length; i++)
            {
                var spool = Path.Combine(stagingRoot, $"nested-{i:D4}.zpak");
                spoolByEntry[nestedCandidates[i]] = spool;
                embeddedSpools.Add((nestedCandidates[i].FullName, spool));
            }

            try
            {
                declaredAggregate = 0;
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    declaredAggregate = checked(declaredAggregate.Value + entry.Length);
                }
            }
            catch (OverflowException)
            {
                incomplete.Add("Aggregate declared expanded size overflowed the supported reporting range.");
            }

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var isDirectory = IsDirectory(entry);
                var normalized = ArchivePathAnalysis.Normalize(entry.FullName);
                var filter = ArchivePathAnalysis.ApplyFilter(role, normalized, isDirectory);
                var stagedNested = spoolByEntry.TryGetValue(entry, out var nestedSpool) ? nestedSpool : null;
                long? actual = null;
                var status = "not-measured";
                string? error = null;

                if (entry.FullName.Length > budget.MaxEntryNameCharacters)
                {
                    const string nameLimitError = "Entry name exceeded the configured report-name limit; this entry and subsequent entries were not measured.";
                    entries.Add(new(
                        OriginalName: entry.FullName[..budget.MaxEntryNameCharacters] + "… [truncated]",
                        NormalizedPath: "",
                        IsDirectory: isDirectory,
                        DeclaredCompressedBytes: entry.CompressedLength,
                        DeclaredExpandedBytes: entry.Length,
                        ActualExpandedBytes: null,
                        MeasurementStatus: "incomplete-report-budget",
                        WouldExtract: null,
                        FilterDecision: "Not evaluated because the entry name exceeded the reporting limit.",
                        Error: nameLimitError));
                    incomplete.Add($"Entry name length exceeded the configured limit of {budget.MaxEntryNameCharacters} characters; remaining entries were not measured.");
                    break;
                }

                try
                {
                    await using var nestedOutput = stagedNested is null ? null : new FileStream(
                        stagedNested, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    progress?.Report($"entry-read-start:{role}:{entry.FullName}");
                    await using var source = entry.Open();
                    var measure = await DrainEntryAsync(source, nestedOutput, measuredAggregate, budget,
                        cancellationToken, progress, role, entry.FullName);
                    actual = measure.BytesRead;
                    measuredAggregate = checked(measuredAggregate + measure.BytesRead);
                    status = isDirectory && measure.Complete ? "complete-directory-entry" : measure.Complete ? "complete" : "incomplete-safety-budget";
                    if (!measure.Complete)
                    {
                        error = measure.Reason;
                        incomplete.Add($"Entry '{entry.FullName}': {measure.Reason}");
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or OverflowException)
                {
                    status = "incomplete-error";
                    error = BoundedDiagnostic(ex);
                    incomplete.Add($"Entry '{entry.FullName}': {error}");
                }

                var measurement = new EntryMeasurement(
                    OriginalName: entry.FullName,
                    NormalizedPath: normalized,
                    IsDirectory: isDirectory,
                    DeclaredCompressedBytes: entry.CompressedLength,
                    DeclaredExpandedBytes: entry.Length,
                    ActualExpandedBytes: actual,
                    MeasurementStatus: status,
                    WouldExtract: filter.Extract,
                    FilterDecision: filter.Reason,
                    Error: error);
                entries.Add(measurement);
                if (filter.Extract && !isDirectory) predicted.Add(normalized);
                if (incomplete.Any(reason => reason.StartsWith($"Entry '{entry.FullName}':", StringComparison.Ordinal)))
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            incomplete.Add("Measurement stopped because the configured scan timeout or user cancellation was reached.");
        }
        catch (InvalidDataException ex)
        {
            incomplete.Add($"Archive data is invalid: {BoundedDiagnostic(ex)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
        {
            incomplete.Add($"Archive reader could not complete: {BoundedDiagnostic(ex)}");
        }

        progress?.Report($"path-analysis-start:{role}");
        var analysis = ArchivePathAnalysis.Analyze(entries, budget, pathWorkBudget, cancellationToken);
        findings.AddRange(analysis.Findings);
        if (!analysis.Complete) incomplete.AddRange(analysis.IncompleteReasons);
        if (role == ExtractionRole.RuntimeApk)
        {
            for (var i = 0; i < embeddedSpools.Count; i++)
            {
                var candidate = embeddedSpools[i];
                if (cancellationToken.IsCancellationRequested)
                {
                    const string cancellationReason = "Not measured because cancellation or the configured scan timeout stopped embedded-ZPAK processing.";
                    for (; i < embeddedSpools.Count; i++)
                    {
                        var unmeasured = embeddedSpools[i];
                        embedded.Add(new(unmeasured.EntryName, cancellationReason,
                            Unmeasured(ExtractionRole.EmbeddedDefaultZpak,
                                $"{fullPath}!/{unmeasured.EntryName}", cancellationReason)));
                        incomplete.Add($"Embedded ZPAK '{unmeasured.EntryName}' was not measured because cancellation or the configured scan timeout stopped processing.");
                    }
                    break;
                }

                var selection = embeddedSpools.Count == 1
                    ? "only candidate; Java will select this archive"
                    : "candidate measured; Java selection order is unverified";
                progress?.Report($"embedded-start:{candidate.EntryName}");
                var nestedReport = await MeasureArchiveAsync(candidate.SpoolPath, ExtractionRole.EmbeddedDefaultZpak,
                    budget, stagingRoot, pathWorkBudget, cancellationToken, $"{fullPath}!/{candidate.EntryName}", progress);
                embedded.Add(new(candidate.EntryName, selection, nestedReport));
                if (!nestedReport.IsComplete())
                {
                    var nestedReasons = nestedReport.IncompleteReasons.Count > 0
                        ? string.Join("; ", nestedReport.IncompleteReasons.Take(3))
                        : "nested measurement was incomplete";
                    incomplete.Add($"Embedded ZPAK '{candidate.EntryName}' was incomplete: {nestedReasons}");
                }
            }
        }

        return new(
            InputPath: reportPath,
            Role: role.ToString(),
            Status: incomplete.Count == 0 ? "complete" : "incomplete",
            CompressedBytes: compressedBytes,
            CentralDirectoryEntryCount: centralCount,
            CentralDirectoryOffset: centralDirectoryOffset,
            CentralDirectoryBytes: centralDirectoryBytes,
            DeclaredAggregateExpandedBytes: declaredAggregate,
            MeasuredExpandedBytes: measuredAggregate,
            AggregateExpandedMeasurementComplete: incomplete.Count == 0,
            Entries: entries,
            PathFindings: findings,
            PredictedExtractedPaths: predicted,
            EmbeddedArchives: embedded,
            Notes: notes,
            IncompleteReasons: incomplete);
    }

    private static async Task<(long BytesRead, bool Complete, string? Reason)> DrainEntryAsync(
        Stream source,
        Stream? duplicateArchiveSpool,
        long priorAggregate,
        ScanBudget budget,
        CancellationToken cancellationToken,
        IProgress<string>? progress,
        ExtractionRole role,
        string entryName)
    {
        var buffer = new byte[16 * 1024];
        long readTotal = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) return (readTotal, true, null);

            readTotal = checked(readTotal + read);
            if (readTotal > budget.MaxEntryExpandedBytes)
                return (readTotal, false, $"Actual expanded bytes exceeded scanner per-entry safety budget {budget.MaxEntryExpandedBytes}.");
            if (priorAggregate + readTotal > budget.MaxAggregateExpandedBytes)
                return (readTotal, false, $"Actual expanded bytes exceeded scanner aggregate safety budget {budget.MaxAggregateExpandedBytes}.");

            if (duplicateArchiveSpool is not null)
                await duplicateArchiveSpool.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            progress?.Report($"entry-read-chunk:{role}:{entryName}:{readTotal}");
        }
    }

    private static bool IsTopLevelAssetsZpak(ZipArchiveEntry entry)
    {
        var name = entry.FullName.Replace('\\', '/');
        var segments = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 2
            && string.Equals(segments[0], "assets", StringComparison.OrdinalIgnoreCase)
            && segments[1].EndsWith(".zpak", StringComparison.OrdinalIgnoreCase)
            && !IsDirectory(entry);
    }

    private static bool IsDirectory(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\') || entry.Name.Length == 0;

    private static ArchiveMeasurementReport Incomplete(
        string path,
        ExtractionRole role,
        string reason,
        long? compressed = null,
        long? count = null,
        long? centralDirectoryOffset = null,
        long? centralDirectoryBytes = null) => new(
            ReportPath(path), role.ToString(), "incomplete", compressed, count,
            centralDirectoryOffset, centralDirectoryBytes, null, 0, false, [], [], [], [], [], [reason]);

    private static ArchiveMeasurementReport Unmeasured(
        ExtractionRole role,
        string displayPath,
        string reason) => new(
            displayPath, role.ToString(), "unmeasured", null, null,
        null, null, null, 0, false, [], [], [], [], [], [reason]);

    private static string BoundedDiagnostic(Exception exception)
    {
        const int maximumMessageCharacters = 256;
        var message = exception.Message ?? string.Empty;
        var bounded = new StringBuilder(Math.Min(message.Length, maximumMessageCharacters));
        for (var i = 0; i < message.Length && i < maximumMessageCharacters; i++)
            bounded.Append(char.IsControl(message[i]) ? ' ' : message[i]);
        if (message.Length > maximumMessageCharacters) bounded.Append('…');
        return $"{exception.GetType().Name}: {bounded}";
    }

    private static string ReportPath(string path)
    {
        if (path.Contains("!/", StringComparison.Ordinal)) return path;
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return path; }
    }

    private static void ValidateBudget(ScanBudget budget)
    {
        var hardMaximum = ScanBudget.Default;
        if (budget.MaxArchiveBytes <= 0 || budget.MaxExternalZpakBytes <= 0 || budget.MaxEntryCount <= 0
            || budget.MaxEntryExpandedBytes <= 0 || budget.MaxAggregateExpandedBytes <= 0
            || budget.MaxDuration <= TimeSpan.Zero || budget.MaxPathAnalysisWorkItems <= 0
            || budget.MaxEntryNameCharacters <= 0 || budget.MaxFindingCount <= 0
            || budget.MaxFindingParticipants <= 0 || budget.MaxReportBytes < 4096
            || budget.MaxArchiveBytes > hardMaximum.MaxArchiveBytes
            || budget.MaxExternalZpakBytes > ScanBudget.ExternalZpakCompressedBytesCeiling
            || budget.MaxEntryCount > hardMaximum.MaxEntryCount
            || budget.MaxEntryExpandedBytes > hardMaximum.MaxEntryExpandedBytes
            || budget.MaxAggregateExpandedBytes > hardMaximum.MaxAggregateExpandedBytes
            || budget.MaxDuration > hardMaximum.MaxDuration
            || budget.MaxPathAnalysisWorkItems > hardMaximum.MaxPathAnalysisWorkItems
            || budget.MaxEntryNameCharacters > hardMaximum.MaxEntryNameCharacters
            || budget.MaxFindingCount > hardMaximum.MaxFindingCount
            || budget.MaxFindingParticipants > hardMaximum.MaxFindingParticipants
            || budget.MaxReportBytes > hardMaximum.MaxReportBytes)
            throw new ArgumentOutOfRangeException(nameof(budget), "Safety budgets must be positive and cannot exceed the utility's fixed operational ceilings.");
    }
}
