using System.Buffers.Binary;

namespace PSXperia.RuntimeArchiveMeasure;

internal sealed record ZipProbeResult(
    bool Complete,
    long? EntryCount,
    long? CentralDirectoryOffset,
    long? CentralDirectoryBytes,
    string? Reason);

internal static class ZipDirectoryProbe
{
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const uint CentralDirectoryFileHeaderSignature = 0x02014B50;
    private const uint Zip64EndOfCentralDirectorySignature = 0x06064B50;
    private const uint Zip64EndOfCentralDirectoryLocatorSignature = 0x07064B50;
    private const int EndRecordLength = 22;
    private const int MaxCommentLength = ushort.MaxValue;
    private const int CentralDirectoryFileHeaderLength = 46;

    // Reads ZIP32 central-directory records before ZipArchive allocates entry
    // objects. ZIP64 and multi-disk archives remain unsupported and incomplete.
    public static ZipProbeResult Probe(
        Stream stream,
        long fileLength,
        int maxEntries,
        int maxEntryNameCharacters,
        long maxCentralDirectoryBytes,
        CancellationToken cancellationToken = default)
    {
        if (!stream.CanSeek || fileLength < 0 || fileLength > stream.Length)
            return Incomplete(null, null, null, "Archive stream length or seek support is invalid.");
        if (maxEntries <= 0 || maxEntryNameCharacters <= 0 || maxCentralDirectoryBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntries), "ZIP preflight limits must be positive.");

        cancellationToken.ThrowIfCancellationRequested();
        var tailLength = (int)Math.Min(fileLength, EndRecordLength + MaxCommentLength);
        if (tailLength < EndRecordLength)
            return Incomplete(null, null, null, "File is too short to contain a ZIP end-of-central-directory record.");

        var tail = new byte[tailLength];
        stream.Position = fileLength - tailLength;
        stream.ReadExactly(tail);
        cancellationToken.ThrowIfCancellationRequested();

        var found = -1;
        for (var i = tail.Length - EndRecordLength; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i, 4)) != EndOfCentralDirectorySignature)
                continue;
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20, 2));
            if (i + EndRecordLength + commentLength == tail.Length)
            {
                found = i;
                break;
            }
        }

        if (found < 0)
            return Incomplete(null, null, null, "A valid ZIP end-of-central-directory record was not found in the permitted trailing window.");

        var record = tail.AsSpan(found);
        if (found >= 20
            && BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(found - 20, 4)) == Zip64EndOfCentralDirectoryLocatorSignature)
        {
            var locator = tail.AsSpan(found - 20, 20);
            var zip64Disk = BinaryPrimitives.ReadUInt32LittleEndian(locator[4..]);
            var zip64RecordOffset = BinaryPrimitives.ReadUInt64LittleEndian(locator[8..]);
            var diskCount = BinaryPrimitives.ReadUInt32LittleEndian(locator[16..]);
            if (zip64Disk != 0 || diskCount != 1)
                return Incomplete(null, null, null, "Multi-disk ZIP64 archives are not measured by this utility.");
            if (zip64RecordOffset > (ulong)fileLength || 56UL > (ulong)fileLength - zip64RecordOffset)
                return Incomplete(null, null, null, "ZIP64 locator points outside the archive; measurement is incomplete.");

            var originalPosition = stream.Position;
            Span<byte> zip64Header = stackalloc byte[56];
            cancellationToken.ThrowIfCancellationRequested();
            stream.Position = (long)zip64RecordOffset;
            stream.ReadExactly(zip64Header);
            stream.Position = originalPosition;
            if (BinaryPrimitives.ReadUInt32LittleEndian(zip64Header) != Zip64EndOfCentralDirectorySignature
                || BinaryPrimitives.ReadUInt64LittleEndian(zip64Header[4..]) < 44)
                return Incomplete(null, null, null, "ZIP64 locator was present but its end-of-central-directory record is invalid.");

            var totalZip64Entries = BinaryPrimitives.ReadUInt64LittleEndian(zip64Header[32..]);
            var zip64Count = totalZip64Entries <= long.MaxValue ? (long?)totalZip64Entries : null;
            return Incomplete(zip64Count, null, null,
                "ZIP64 end-of-central-directory locator and record detected; this utility's bounded ZIP32 preflight does not support ZIP64.");
        }

        var diskNumber = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
        var centralDirectoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
        var entriesOnDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
        var totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
        var centralDirectoryBytes32 = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
        var centralDirectoryOffset32 = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
        var count = (long)totalEntries;
        var directoryOffset = (long)centralDirectoryOffset32;
        var directoryBytes = (long)centralDirectoryBytes32;

        if (diskNumber != 0 || centralDirectoryDisk != 0 || entriesOnDisk != totalEntries)
            return Incomplete(count, directoryOffset, directoryBytes, "Multi-disk ZIP archives are not measured by this utility.");
        if (totalEntries == ushort.MaxValue || centralDirectoryBytes32 == uint.MaxValue || centralDirectoryOffset32 == uint.MaxValue)
            return Incomplete(null, null, null, "ZIP64 archive detected; this utility's bounded ZIP32 preflight does not support it.");
        if (totalEntries > maxEntries)
            return Incomplete(count, directoryOffset, directoryBytes,
                $"Central-directory entry count {totalEntries} exceeds the scanner safety budget {maxEntries}.");
        var absoluteRecordOffset = fileLength - tailLength + found;
        var directoryEnd = (ulong)centralDirectoryOffset32 + centralDirectoryBytes32;
        if (centralDirectoryOffset32 > fileLength || directoryEnd > (ulong)fileLength)
            return Incomplete(count, directoryOffset, directoryBytes,
                "Central-directory offset or length points outside the archive.");
        if (directoryEnd > (ulong)absoluteRecordOffset)
            return Incomplete(count, directoryOffset, directoryBytes,
                "Central-directory bounds extend beyond the end-of-central-directory record.");
        if (directoryBytes > maxCentralDirectoryBytes)
            return Incomplete(count, directoryOffset, directoryBytes,
                $"Central-directory byte length {directoryBytes} exceeds the fixed scanner safety ceiling {maxCentralDirectoryBytes}.");

        var directoryPosition = (long)centralDirectoryOffset32;
        ulong consumed = 0;
        var header = new byte[CentralDirectoryFileHeaderLength];
        // Four bytes per reporting character covers UTF-8's maximum encoded
        // scalar length; name bytes are bounded without decoding or allocation.
        var maximumNameBytes = checked((long)maxEntryNameCharacters * 4);

        for (var index = 0; index < totalEntries; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = (ulong)centralDirectoryBytes32 - consumed;
            if (remaining < CentralDirectoryFileHeaderLength)
                return Incomplete(count, directoryOffset, directoryBytes,
                    $"Central directory ended before the declared entry count; record {index + 1} has a truncated fixed header.");

            stream.Position = directoryPosition;
            stream.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != CentralDirectoryFileHeaderSignature)
                return Incomplete(count, directoryOffset, directoryBytes,
                    $"Central-directory record {index + 1} has an invalid file-header signature.");

            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28, 2));
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(30, 2));
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(32, 2));
            var diskStart = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(34, 2));
            if (diskStart != 0)
                return Incomplete(count, directoryOffset, directoryBytes,
                    $"Central-directory record {index + 1} refers to another disk; multi-disk archives are not measured.");
            if (nameLength > maximumNameBytes)
                return Incomplete(count, directoryOffset, directoryBytes,
                    $"Central-directory record {index + 1} filename byte length {nameLength} exceeds the scanner name limit.");

            var recordLength = (ulong)CentralDirectoryFileHeaderLength + nameLength + extraLength + commentLength;
            if (recordLength > remaining)
                return Incomplete(count, directoryOffset, directoryBytes,
                    $"Central-directory record {index + 1} filename, extra-field, or comment length extends beyond the declared directory boundary.");

            consumed += recordLength;
            directoryPosition = checked(directoryPosition + (long)recordLength);
        }

        if (consumed != centralDirectoryBytes32)
            return Incomplete(count, directoryOffset, directoryBytes,
                $"Central-directory record count {totalEntries} does not account for its declared {centralDirectoryBytes32}-byte length.");

        return new(true, count, directoryOffset, directoryBytes, null);
    }

    private static ZipProbeResult Incomplete(long? count, long? offset, long? bytes, string reason) =>
        new(false, count, offset, bytes, reason);
}
