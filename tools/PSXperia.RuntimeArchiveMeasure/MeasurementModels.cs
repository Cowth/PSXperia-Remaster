namespace PSXperia.RuntimeArchiveMeasure;

public sealed record ScanBudget(
    long MaxArchiveBytes,
    int MaxEntryCount,
    long MaxEntryExpandedBytes,
    long MaxAggregateExpandedBytes,
    TimeSpan MaxDuration,
    long MaxPathAnalysisWorkItems,
    int MaxEntryNameCharacters,
    int MaxFindingCount,
    int MaxFindingParticipants,
    long MaxReportBytes)
{
    // Fixed measurement-only ceiling derived from the supplied external ZPAK's
    // observed compressed size. It is not a conversion or extraction limit.
    public const long ExternalZpakCompressedBytesCeiling = 551_460_579;

    // ZIP32 central-directory ceiling for preflight before ZipArchive allocates
    // entry objects. The largest supplied Crash Bandicoot reference directory
    // measured 4,524 bytes; 8 MiB gives over 1,800x headroom while remaining a
    // bounded scanner-only limit. Other runtime profiles remain unverified.
    public const long MaxCentralDirectoryBytes = 8L * 1024 * 1024;

    // This limit may be lowered by a caller, but validation rejects attempts to
    // raise it above the fixed ceiling. The CLI exposes no option to change it.
    public long MaxExternalZpakBytes { get; init; } = ExternalZpakCompressedBytesCeiling;

    // Operational scanner guardrails only. These are not valid-runtime limits
    // and must never be used to accept or reject conversion inputs.
    public static ScanBudget Default { get; } = new(
        MaxArchiveBytes: 512L * 1024 * 1024,
        MaxEntryCount: 100_000,
        MaxEntryExpandedBytes: 1024L * 1024 * 1024,
        MaxAggregateExpandedBytes: 2L * 1024 * 1024 * 1024,
        MaxDuration: TimeSpan.FromMinutes(5),
        MaxPathAnalysisWorkItems: 2_000_000,
        MaxEntryNameCharacters: 4096,
        MaxFindingCount: 10_000,
        MaxFindingParticipants: 32,
        MaxReportBytes: 16L * 1024 * 1024);

    public long CompressedArchiveLimitFor(ExtractionRole role) =>
        role == ExtractionRole.ExternalRuntimeZpak ? MaxExternalZpakBytes : MaxArchiveBytes;

    public bool AllowsCompressedArchive(ExtractionRole role, long compressedBytes) =>
        compressedBytes >= 0 && compressedBytes <= CompressedArchiveLimitFor(role);
}

public enum ExtractionRole
{
    RuntimeApk,
    EmbeddedDefaultZpak,
    ExternalRuntimeZpak
}

public sealed record PathFinding(string Kind, string Path, IReadOnlyList<string> Entries, string Details,
    bool ParticipantsTruncated = false);

public sealed record EntryMeasurement(
    string OriginalName,
    string NormalizedPath,
    bool IsDirectory,
    long DeclaredCompressedBytes,
    long DeclaredExpandedBytes,
    long? ActualExpandedBytes,
    string MeasurementStatus,
    bool? WouldExtract,
    string FilterDecision,
    string? Error = null);

public sealed record EmbeddedArchiveMeasurement(
    string EntryPath,
    string SelectionStatus,
    ArchiveMeasurementReport Measurement);

public sealed record ArchiveMeasurementReport(
    string InputPath,
    string Role,
    string Status,
    long? CompressedBytes,
    long? CentralDirectoryEntryCount,
    long? CentralDirectoryOffset,
    long? CentralDirectoryBytes,
    long? DeclaredAggregateExpandedBytes,
    long MeasuredExpandedBytes,
    bool AggregateExpandedMeasurementComplete,
    IReadOnlyList<EntryMeasurement> Entries,
    IReadOnlyList<PathFinding> PathFindings,
    IReadOnlyList<string> PredictedExtractedPaths,
    IReadOnlyList<EmbeddedArchiveMeasurement> EmbeddedArchives,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> IncompleteReasons);

public sealed record MeasurementReport(
    string UtilityVersion,
    DateTimeOffset MeasuredAtUtc,
    string Runtime,
    ScanBudget ScanSafetyBudget,
    string ScopeNote,
    string TemporaryStagingCleanupStatus,
    ArchiveMeasurementReport Apk,
    ArchiveMeasurementReport ExternalZpak,
    bool ReportTruncated = false,
    IReadOnlyList<string>? ReportIncompleteReasons = null)
{
    public bool IsComplete => !ReportTruncated
        && string.Equals(TemporaryStagingCleanupStatus, "complete", StringComparison.Ordinal)
        && Apk.IsComplete()
        && ExternalZpak.IsComplete();
}

public static class ArchiveReportExtensions
{
    public static bool IsComplete(this ArchiveMeasurementReport report) =>
        string.Equals(report.Status, "complete", StringComparison.Ordinal)
        && report.AggregateExpandedMeasurementComplete
        && report.EmbeddedArchives.All(x => x.Measurement.IsComplete());
}

public static class MeasurementExitCode
{
    public static int For(MeasurementReport report) => report.IsComplete ? 0 : 2;
}
