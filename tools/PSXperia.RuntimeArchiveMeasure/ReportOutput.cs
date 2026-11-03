using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PSXperia.RuntimeArchiveMeasure;

public static class ReportOutput
{
    private const long AbsoluteMaximumReportBytes = 16L * 1024 * 1024;
    private const long MinimumReportBytes = 4096;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ToJson(MeasurementReport report) => ToBoundedJson(report).Json;

    public static (string Json, MeasurementReport Report) ToBoundedJson(MeasurementReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var reportLimit = ValidateReportLimit(report.ScanSafetyBudget.MaxReportBytes);
        try { return (SerializeBounded(report, reportLimit), report); }
        catch (ReportSizeLimitException)
        {
            const string reason = "The JSON report exceeded the configured report-size limit; detailed entry and finding lists were omitted.";
            var reduced = MarkReportTruncated(report, reason);
            return (SerializeBounded(reduced, reportLimit), reduced);
        }
    }

    public static async Task<MeasurementReport> SaveJsonAsync(MeasurementReport report, string path, bool allowSharedOutput,
        CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(path);
        if (!allowSharedOutput && IsRemotePath(full))
            throw new InvalidOperationException("Report path is on a network location. Add --allow-shared-output only if you explicitly intend to share the report.");
        if (!allowSharedOutput && HasExistingReparseComponent(full))
            throw new InvalidOperationException("Report path traverses a symbolic link or reparse point. Choose a direct local path or explicitly opt into a shared destination.");

        var (json, resultingReport) = ToBoundedJson(report);
        var directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
        var created = false;
        try
        {
            await using var output = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            created = true;
            await output.WriteAsync(bytes, cancellationToken);
            await output.FlushAsync(cancellationToken);
            return resultingReport;
        }
        catch
        {
            if (created)
            {
                try { File.Delete(full); } catch { /* leave the primary report-write error visible */ }
            }
            throw;
        }
    }

    public static bool IsRemotePath(string path)
    {
        if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            return true;
        if (OperatingSystem.IsWindows())
        {
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrWhiteSpace(root))
            {
                try { return new DriveInfo(root).DriveType == DriveType.Network; }
                catch { return true; }
            }
        }
        return false;
    }

    public static bool HasExistingReparseComponent(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root)) return true;

        var current = root;
        var relative = fullPath[root.Length..];
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
            if ((attributes & FileAttributes.ReparsePoint) != 0) return true;
        }
        return false;
    }

    private static string SerializeBounded<T>(T value, long limit)
    {
        using var memory = new MemoryStream();
        using (var bounded = new BoundedWriteStream(memory, limit, leaveOpen: true))
            JsonSerializer.SerializeAsync(bounded, value, Options).GetAwaiter().GetResult();
        return Encoding.UTF8.GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
    }

    private static long ValidateReportLimit(long requestedLimit)
    {
        if (requestedLimit < MinimumReportBytes || requestedLimit > AbsoluteMaximumReportBytes)
            throw new ArgumentOutOfRangeException(nameof(requestedLimit),
                $"Report size budget must be between {MinimumReportBytes} and the fixed {AbsoluteMaximumReportBytes}-byte ceiling.");
        return requestedLimit;
    }

    private static MeasurementReport MarkReportTruncated(MeasurementReport report, string reason) => report with
    {
        ReportTruncated = true,
        ReportIncompleteReasons = [reason],
        Apk = MarkArchiveTruncated(report.Apk, reason),
        ExternalZpak = MarkArchiveTruncated(report.ExternalZpak, reason)
    };

    private static ArchiveMeasurementReport MarkArchiveTruncated(ArchiveMeasurementReport archive, string reason) => archive with
    {
        InputPath = Path.GetFileName(archive.InputPath),
        Status = "incomplete",
        AggregateExpandedMeasurementComplete = false,
        Entries = [],
        PathFindings = [],
        PredictedExtractedPaths = [],
        EmbeddedArchives = [],
        Notes = [],
        IncompleteReasons = [reason]
    };

    private sealed class ReportSizeLimitException : IOException { }

    private sealed class BoundedWriteStream(Stream inner, long maximumBytes, bool leaveOpen) : Stream
    {
        private long _written;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _written;
        public override long Position { get => _written; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureWithinLimit(count);
            inner.Write(buffer, offset, count);
            _written += count;
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureWithinLimit(buffer.Length);
            inner.Write(buffer);
            _written += buffer.Length;
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            EnsureWithinLimit(buffer.Length);
            await inner.WriteAsync(buffer, cancellationToken);
            _written += buffer.Length;
        }
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            EnsureWithinLimit(count);
            await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
            _written += count;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !leaveOpen) inner.Dispose();
            base.Dispose(disposing);
        }
        private void EnsureWithinLimit(int incoming)
        {
            if (incoming < 0 || incoming > maximumBytes - _written) throw new ReportSizeLimitException();
        }
    }
}
