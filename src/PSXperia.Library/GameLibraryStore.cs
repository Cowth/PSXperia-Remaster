using System.Text.Json;
using System.Text.RegularExpressions;
using PSXperia.Engine;

namespace PSXperia.Library;

/// <summary>
/// Stores the user's library in LocalAppData. Loading is deliberately read-only:
/// opening the app must never rewrite a user's library just to normalize metadata.
/// </summary>
public sealed class GameLibraryStore
{
    private readonly string _libraryFile;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public GameLibraryStore(string? libraryFile = null)
    {
        var appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PSXperiaRemastered");
        _libraryFile = libraryFile ?? Path.Combine(appDirectory, "library.json");
    }

    public async Task<IReadOnlyList<GameEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_libraryFile))
            return Array.Empty<GameEntry>();

        try
        {
            // Permit another process (backup/indexer) to read the file concurrently.
            await using var stream = new FileStream(
                _libraryFile, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);

            var entries = await JsonSerializer.DeserializeAsync<List<GameEntry>>(
                stream, _jsonOptions, cancellationToken) ?? new List<GameEntry>();

            // Normalize in memory only. Never write during startup/library refresh.
            return NormalizeLegacyEntries(entries);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException(
                $"Windows denied read access to the library file '{_libraryFile}'. " +
                "Check the file's Security permissions and close any program that may be locking it.", ex);
        }
        catch (IOException ex)
        {
            throw new IOException($"Could not read the library file '{_libraryFile}': {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"The library file contains invalid JSON: '{_libraryFile}'. " +
                "Make a backup before attempting any repair. Details: {ex.Message}", ex);
        }
    }

    public async Task SaveAsync(IEnumerable<GameEntry> entries, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_libraryFile)
            ?? throw new InvalidOperationException("The library file has no parent directory.");
        Directory.CreateDirectory(directory);

        // Use a unique temp file so stale/locked library.json.tmp files cannot block saving.
        var temporary = Path.Combine(directory, $"library.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, entries.ToList(), _jsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            // Windows refuses replacement of a read-only destination. Clear only that
            // attribute; do not change ACLs or take ownership of the user's files.
            if (File.Exists(_libraryFile))
            {
                var attributes = File.GetAttributes(_libraryFile);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(_libraryFile, attributes & ~FileAttributes.ReadOnly);
            }

            File.Move(temporary, _libraryFile, overwrite: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException(
                $"Windows denied permission to save the library in '{directory}'. " +
                "Check the folder's Security permissions and ensure library.json is not open in another app.", ex);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { /* A leftover uniquely named temp file is harmless; preserve the original error. */ }
        }
    }

    public async Task<GameEntry> ImportAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        // Inspect the selected image before changing the library.
        var inspection = DiscImageInspector.Inspect(imagePath);
        var existing = (await LoadAsync(cancellationToken)).ToList();
        var normalizedPath = Path.GetFullPath(inspection.EntryPath);

        // A BIN/IMG selected after its CUE was imported is not a new game.
        // Resolve it to the existing CUE parent before considering standalone import.
        var extension = Path.GetExtension(normalizedPath);
        if (extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".img", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var cueEntry in existing.Where(x => Path.GetExtension(x.ImagePath)
                         .Equals(".cue", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    if (!File.Exists(cueEntry.ImagePath)) continue;
                    var cueInspection = DiscImageInspector.Inspect(cueEntry.ImagePath);
                    if (cueInspection.DataFiles.Any(p => string.Equals(Path.GetFullPath(p), normalizedPath,
                            StringComparison.OrdinalIgnoreCase)))
                        return cueEntry;
                }
                catch (IOException) { /* An unavailable CUE cannot claim this raw image. */ }
                catch (UnauthorizedAccessException) { /* Keep checking other CUE entries. */ }
                catch (InvalidDataException) { /* Ignore malformed legacy CUE entries. */ }
            }
        }

        var duplicateIndex = existing.FindIndex(x =>
            string.Equals(Path.GetFullPath(x.ImagePath), normalizedPath, StringComparison.OrdinalIgnoreCase));

        if (duplicateIndex >= 0)
        {
            var old = existing[duplicateIndex];
            var serial = old.DiscSerial ?? DiscSerialScanner.FindSerial(inspection);
            var refreshed = ApplyInspection(old, inspection) with
            {
                DiscSerial = serial,
                TitleId = serial ?? old.TitleId ?? ExtractTitleId(old.Title),
                Region = old.Region ?? DetectRegion((serial ?? string.Empty) + " " + old.Title + " " + normalizedPath)
            };
            existing[duplicateIndex] = refreshed;
            var normalizedExisting = NormalizeLegacyEntries(existing);
            await SaveAsync(normalizedExisting, cancellationToken);
            return normalizedExisting.FirstOrDefault(x => x.Id == refreshed.Id) ??
                   normalizedExisting.FirstOrDefault(x => string.Equals(Path.GetFullPath(x.ImagePath), normalizedPath, StringComparison.OrdinalIgnoreCase)) ??
                   refreshed;
        }

        var title = Path.GetFileNameWithoutExtension(normalizedPath);
        var discSerial = DiscSerialScanner.FindSerial(inspection);
        var titleId = discSerial ?? ExtractTitleId(title);
        var entry = new GameEntry(Guid.NewGuid(), title, titleId, normalizedPath, null,
            CompatibilityStatus.Untested, DateTime.UtcNow)
        {
            DiscSerial = discSerial,
            Region = DetectRegion((discSerial ?? string.Empty) + " " + title + " " + normalizedPath),
            TrackCount = inspection.Tracks.Count,
            DataTrackCount = inspection.DataTrackCount,
            AudioTrackCount = inspection.AudioTrackCount,
            Tracks = inspection.Tracks.Select(t => new GameTrackEntry(
                t.Number, t.FilePath, t.Mode.ToString(), t.Index00, t.Index01)).ToArray()
        };

        existing.Add(entry);
        var normalizedEntries = NormalizeLegacyEntries(existing);
        await SaveAsync(normalizedEntries, cancellationToken);
        return normalizedEntries.FirstOrDefault(x => x.Id == entry.Id) ??
               normalizedEntries.FirstOrDefault(x => string.Equals(Path.GetFullPath(x.ImagePath), normalizedPath, StringComparison.OrdinalIgnoreCase)) ??
               entry;
    }


    public async Task<bool> RemoveAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        var entries = (await LoadAsync(cancellationToken)).ToList();
        var removed = entries.RemoveAll(x => x.Id == gameId) > 0;
        if (removed)
            await SaveAsync(entries, cancellationToken);
        return removed;
    }

    public async Task UpdateCoverAsync(Guid gameId, string coverPath, bool isManual = true, string? source = null, CancellationToken cancellationToken = default)
    {
        var entries = (await LoadAsync(cancellationToken)).ToList();
        var index = entries.FindIndex(x => x.Id == gameId);
        if (index < 0)
            throw new InvalidOperationException("The selected game is no longer in the library.");

        entries[index] = entries[index] with { CoverPath = Path.GetFullPath(coverPath), CoverIsManual = isManual, CoverSource = source };
        await SaveAsync(entries, cancellationToken);
    }

    private static GameEntry ApplyInspection(GameEntry entry, DiscImageInspection inspection) =>
        entry with
        {
            TrackCount = inspection.Tracks.Count,
            DataTrackCount = inspection.DataTrackCount,
            AudioTrackCount = inspection.AudioTrackCount,
            Tracks = inspection.Tracks.Select(t => new GameTrackEntry(
                t.Number, t.FilePath, t.Mode.ToString(), t.Index00, t.Index01)).ToArray()
        };

    private static List<GameEntry> NormalizeLegacyEntries(List<GameEntry> entries)
    {
        var result = entries.ToList();
        var cueEntries = result.Where(x => Path.GetExtension(x.ImagePath)
            .Equals(".cue", StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var cueEntry in cueEntries)
        {
            var index = result.FindIndex(x => x.Id == cueEntry.Id);
            if (index < 0) continue;

            DiscImageInspection? inspection = null;
            if (File.Exists(cueEntry.ImagePath))
            {
                try { inspection = DiscImageInspector.Inspect(cueEntry.ImagePath); }
                catch { /* Keep saved metadata; unavailable source files must not break startup. */ }
            }

            // Old builds sometimes imported every referenced BIN as a separate game.
            // Prefer exact paths from a readable CUE; fall back to the legacy '(Track N)' title pattern
            // when the user has moved the complete disc folder since that import.
            var referencedPaths = inspection is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(inspection.DataFiles.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
            var parentTitle = Path.GetFileNameWithoutExtension(cueEntry.ImagePath);
            var duplicates = result.Where(x => x.Id != cueEntry.Id &&
                (referencedPaths.Contains(SafeFullPath(x.ImagePath)) ||
                 IsLegacyTrackForParent(x.Title, parentTitle, cueEntry.ImagePath, x.ImagePath, inspection)))
                .ToList();

            var cover = !string.IsNullOrWhiteSpace(cueEntry.CoverPath)
                ? cueEntry.CoverPath
                : duplicates.Select(x => x.CoverPath).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

            var updated = cueEntry with { CoverPath = cover };
            if (inspection is not null)
            {
                var serial = cueEntry.DiscSerial ?? DiscSerialScanner.FindSerial(inspection);
                updated = ApplyInspection(updated, inspection) with
                {
                    DiscSerial = serial,
                    TitleId = serial ?? cueEntry.TitleId,
                    Region = cueEntry.Region ?? DetectRegion((serial ?? string.Empty) + " " + cueEntry.Title + " " + cueEntry.ImagePath)
                };
            }
            else if (string.IsNullOrWhiteSpace(updated.Region))
            {
                updated = updated with { Region = DetectRegion(updated.Title + " " + updated.ImagePath) };
            }

            result[index] = updated;
            foreach (var duplicate in duplicates)
                result.RemoveAll(x => x.Id == duplicate.Id);
        }

        // If a user re-imported a moved copy of the same CUE, keep one canonical
        // game record. Prefer the copy whose CUE can currently be inspected and has tracks.
        var duplicateCueGroups = result
            .Where(x => Path.GetExtension(x.ImagePath).Equals(".cue", StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => Path.GetFileNameWithoutExtension(x.ImagePath), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();
        foreach (var group in duplicateCueGroups)
        {
            var ordered = group.OrderByDescending(x => x.TrackCount)
                .ThenByDescending(x => File.Exists(x.ImagePath))
                .ThenBy(x => x.DateAdded)
                .ToList();
            var keeper = ordered[0];
            var inheritedCover = keeper.CoverPath ?? ordered.Select(x => x.CoverPath)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            var inheritedSerial = keeper.DiscSerial ?? ordered.Select(x => x.DiscSerial)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            keeper = keeper with
            {
                CoverPath = inheritedCover,
                DiscSerial = inheritedSerial,
                TitleId = inheritedSerial ?? keeper.TitleId ?? ordered.Select(x => x.TitleId)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                Region = keeper.Region ?? ordered.Select(x => x.Region)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            };
            var keeperIndex = result.FindIndex(x => x.Id == keeper.Id);
            if (keeperIndex >= 0) result[keeperIndex] = keeper;
            foreach (var duplicate in ordered.Skip(1))
                result.RemoveAll(x => x.Id == duplicate.Id);
        }

        // Fill region/product-code metadata for older records without changing the saved file.
        for (var i = 0; i < result.Count; i++)
        {
            var game = result[i];
            var serial = game.DiscSerial ?? ExtractTitleId(game.TitleId ?? game.Title + " " + game.ImagePath);
            var region = game.Region ?? DetectRegion((serial ?? string.Empty) + " " + game.Title + " " + game.ImagePath);
            if (serial != game.DiscSerial || region != game.Region)
                result[i] = game with { DiscSerial = serial, TitleId = serial ?? game.TitleId, Region = region };
        }

        return result;
    }

    private static string SafeFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    private static bool IsLegacyTrackForParent(string trackTitle, string parentTitle, string cuePath,
        string trackPath, DiscImageInspection? inspection)
    {
        var match = Regex.Match(trackTitle, @"^(?<parent>.+?)\s+\(Track\s+\d+\)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !match.Groups["parent"].Value.Equals(parentTitle, StringComparison.OrdinalIgnoreCase))
            return false;

        if (inspection is not null)
            return inspection.DataFiles.Any(p => Path.GetFileName(p).Equals(Path.GetFileName(trackPath), StringComparison.OrdinalIgnoreCase));

        var sameFolder = string.Equals(Path.GetDirectoryName(SafeFullPath(cuePath)),
            Path.GetDirectoryName(SafeFullPath(trackPath)), StringComparison.OrdinalIgnoreCase);
        return sameFolder && (Path.GetExtension(trackPath).Equals(".bin", StringComparison.OrdinalIgnoreCase) ||
               Path.GetExtension(trackPath).Equals(".img", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractTitleId(string value)
    {
        var match = Regex.Match(value.ToUpperInvariant(),
            @"\b(?:SCUS|SLUS|SCES|SLES|SCPS|SLPS|SIPS|PAPX|PBPX|PCPX)-?\d{3,6}\b");
        return match.Success ? match.Value : null;
    }

    private static string? DetectRegion(string value)
    {
        var text = value.ToUpperInvariant();

        if (Regex.IsMatch(text, @"\b(?:SCUS|SLUS|PAPX)-?\d{3,6}(?:\.\d{2})?\b") ||
            Regex.IsMatch(text, @"\bUSA\b|\bU\.S\.A\.\b|\(US\)|\(U\)|\bNTSC[- ]?U\b"))
            return "USA";

        if (Regex.IsMatch(text, @"\b(?:SLES|SCES|PBPX)-?\d{3,6}(?:\.\d{2})?\b") ||
            Regex.IsMatch(text, @"\bEUROPE\b|\bEUR\b|\bPAL\b|\bUK\b"))
            return "Europe";

        if (Regex.IsMatch(text, @"\b(?:SCPS|SLPS|SIPS|PCPX)-?\d{3,6}(?:\.\d{2})?\b") ||
            Regex.IsMatch(text, @"\bJAPAN\b|\bJPN\b|\bNTSC[- ]?J\b"))
            return "Japan";

        return null;
    }
}
