using System.Globalization;
using System.Text.RegularExpressions;

namespace PSXperia.Engine;

public enum DiscTrackMode
{
    Audio,
    Mode1_2048,
    Mode1_2352,
    Mode2_2336,
    Mode2_2352,
    Unknown
}

public sealed record DiscTrack(
    int Number,
    string FilePath,
    DiscTrackMode Mode,
    string? Index00,
    string? Index01);

public sealed record DiscImageInspection(
    string EntryPath,
    string Format,
    long TotalBytes,
    long? SectorCount,
    IReadOnlyList<string> DataFiles,
    IReadOnlyList<DiscTrack> Tracks,
    IReadOnlyList<string> Warnings)
{
    public int DataTrackCount => Tracks.Count(t => t.Mode != DiscTrackMode.Audio);
    public int AudioTrackCount => Tracks.Count(t => t.Mode == DiscTrackMode.Audio);
}

public static class DiscImageInspector
{
    private static readonly Regex FileLine = new(
        "^\\s*FILE\\s+\"(?<quoted>[^\"]+)\"\\s+(?<type>\\S+)|^\\s*FILE\\s+(?<plain>\\S+)\\s+(?<type2>\\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TrackLine = new(
        "^\\s*TRACK\\s+(?<number>\\d+)\\s+(?<mode>\\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IndexLine = new(
        "^\\s*INDEX\\s+(?<index>00|01)\\s+(?<time>\\d{2}:\\d{2}:\\d{2})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static DiscImageInspection Inspect(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("An image path is required.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The selected disc image was not found.", fullPath);

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        return extension switch
        {
            ".cue" => InspectCue(fullPath),
            ".bin" or ".img" or ".iso" => InspectRaw(fullPath),
            _ => throw new NotSupportedException($"The file type '{extension}' is not supported yet.")
        };
    }

    private static DiscImageInspection InspectRaw(string path)
    {
        var info = new FileInfo(path);
        var warnings = new List<string>();
        var ext = Path.GetExtension(path).ToLowerInvariant();
        long? sectors = null;

        if (ext is ".bin" or ".img")
        {
            if (info.Length % 2352 == 0) sectors = info.Length / 2352;
            else if (info.Length % 2448 == 0) sectors = info.Length / 2448;
            else warnings.Add("File size is not an exact multiple of common raw CD sector sizes (2352 or 2448 bytes). It may be truncated or use another layout.");
            warnings.Add("A standalone raw track does not describe the complete disc layout. Import its CUE sheet when one is available.");
        }
        else if (ext == ".iso")
        {
            if (info.Length % 2048 == 0) sectors = info.Length / 2048;
            else warnings.Add("File size is not an exact multiple of 2048-byte sectors. The image may be malformed or use a different layout.");
        }

        return new DiscImageInspection(path, ext.TrimStart('.').ToUpperInvariant(), info.Length,
            sectors, new[] { path }, Array.Empty<DiscTrack>(), warnings);
    }

    private static DiscImageInspection InspectCue(string cuePath)
    {
        var baseDirectory = Path.GetDirectoryName(cuePath)!;
        // Use shared read access so indexing/antivirus readers do not cause an
        // avoidable sharing violation during import.
        string[] lines;
        try
        {
            using var cueStream = new FileStream(cuePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(cueStream, detectEncodingFromByteOrderMarks: true);
            var cueText = reader.ReadToEnd();
            lines = cueText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException(
                $"Windows denied read access to the CUE sheet itself: '{cuePath}'.", ex);
        }
        var files = new List<string>();
        var tracks = new List<DiscTrack>();
        string? currentFile = null;
        var totalBytes = 0L;
        var warnings = new List<string>();

        foreach (var line in lines)
        {
            var fileMatch = FileLine.Match(line);
            if (fileMatch.Success)
            {
                var relative = fileMatch.Groups["quoted"].Success
                    ? fileMatch.Groups["quoted"].Value
                    : fileMatch.Groups["plain"].Value;
                var resolved = Path.GetFullPath(Path.Combine(baseDirectory, relative));
                if (!File.Exists(resolved))
                    throw new FileNotFoundException(
                        $"CUE references a track file that Windows cannot find: '{relative}'. Resolved path: '{resolved}'.",
                        resolved);

                currentFile = resolved;
                if (!files.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                {
                    files.Add(resolved);

                    // Library import should not fail solely because Windows denies
                    // metadata access to a referenced track. Conversion will perform
                    // a real read later and report a blocking error if access is denied.
                    try
                    {
                        using var trackStream = new FileStream(resolved, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete);
                        totalBytes += trackStream.Length;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        warnings.Add($"Windows denied access to file metadata for '{resolved}'. The CUE was indexed, but this track must be readable before conversion.");
                    }
                    catch (IOException ex)
                    {
                        warnings.Add($"Could not read file metadata for '{resolved}': {ex.Message}");
                    }
                }
                continue;
            }

            var trackMatch = TrackLine.Match(line);
            if (trackMatch.Success)
            {
                if (currentFile is null)
                    throw new InvalidDataException("CUE contains a TRACK entry before a FILE entry.");
                if (!int.TryParse(trackMatch.Groups["number"].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out var number))
                    throw new InvalidDataException("CUE contains an invalid track number.");

                tracks.Add(new DiscTrack(number, currentFile, ParseMode(trackMatch.Groups["mode"].Value), null, null));
                continue;
            }

            var indexMatch = IndexLine.Match(line);
            if (indexMatch.Success && tracks.Count > 0)
            {
                var indexNumber = indexMatch.Groups["index"].Value;
                var time = indexMatch.Groups["time"].Value;
                tracks[^1] = indexNumber == "00"
                    ? tracks[^1] with { Index00 = time }
                    : tracks[^1] with { Index01 = time };
            }
        }

        if (files.Count == 0 || tracks.Count == 0)
            throw new InvalidDataException("The CUE sheet does not contain a valid FILE and TRACK definition.");

        if (tracks.Any(t => t.Mode == DiscTrackMode.Unknown))
            warnings.Add("One or more tracks use an unrecognized CUE track mode.");
        if (!tracks.Any(t => t.Mode != DiscTrackMode.Audio))
            warnings.Add("No data track was identified. This may be an audio-only disc.");
        if (tracks.Any(t => string.IsNullOrWhiteSpace(t.Index01)))
            warnings.Add("One or more tracks are missing INDEX 01. The image may not be suitable for conversion.");

        return new DiscImageInspection(cuePath, "CUE", totalBytes, null, files, tracks, warnings);
    }

    private static DiscTrackMode ParseMode(string mode) => mode.ToUpperInvariant() switch
    {
        "AUDIO" => DiscTrackMode.Audio,
        "MODE1/2048" => DiscTrackMode.Mode1_2048,
        "MODE1/2352" => DiscTrackMode.Mode1_2352,
        "MODE2/2336" => DiscTrackMode.Mode2_2336,
        "MODE2/2352" => DiscTrackMode.Mode2_2352,
        _ => DiscTrackMode.Unknown
    };
}
