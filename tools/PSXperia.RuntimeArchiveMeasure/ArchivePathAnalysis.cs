namespace PSXperia.RuntimeArchiveMeasure;

internal sealed record PathAnalysisResult(
    IReadOnlyList<PathFinding> Findings,
    bool Complete,
    IReadOnlyList<string> IncompleteReasons);

internal sealed class PathAnalysisWorkBudget(long maximum)
{
    private long _remaining = maximum;
    public long Used { get; private set; }

    public bool TryConsume(CancellationToken cancellationToken)
        => TryConsume(1, cancellationToken);

    public bool TryConsume(long count, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || count < 0 || _remaining < count) return false;
        _remaining -= count;
        Used += count;
        return true;
    }
}

internal static class ArchivePathAnalysis
{
    public static string Normalize(string name)
    {
        var path = name.Replace('\\', '/');
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part != ".")
            .ToArray();
        return string.Join('/', parts).TrimEnd('/');
    }

    public static PathAnalysisResult Analyze(
        IReadOnlyList<EntryMeasurement> entries,
        ScanBudget budget,
        PathAnalysisWorkBudget workBudget,
        CancellationToken cancellationToken)
    {
        var findings = new List<PathFinding>();
        var reasons = new List<string>();
        var nodes = new Dictionary<string, PathNode>(StringComparer.OrdinalIgnoreCase);
        var complete = true;

        bool Consume(long count = 1)
        {
            if (workBudget.TryConsume(count, cancellationToken)) return true;
            complete = false;
            reasons.Add(cancellationToken.IsCancellationRequested
                ? "Path analysis stopped because cancellation or the measurement timeout was reached."
                : $"Path analysis reached the configured work budget of {workBudget.Used} items.");
            return false;
        }

        bool AddFinding(string kind, string path, PathNode node, string details)
        {
            if (findings.Count >= budget.MaxFindingCount)
            {
                complete = false;
                reasons.Add($"Path finding count exceeded the configured reporting limit of {budget.MaxFindingCount}.");
                return false;
            }

            var samples = node.GetSamples(budget.MaxFindingParticipants, out var truncated);
            if (truncated)
            {
                complete = false;
                reasons.Add($"Participant names for path finding '{kind}' at '{path}' exceeded the configured sample limit of {budget.MaxFindingParticipants}.");
            }
            findings.Add(new(kind, path, samples, details, truncated));
            return true;
        }

        foreach (var entry in entries)
        {
            if (!Consume()) return new(findings, false, reasons);
            if (entry.MeasurementStatus == "incomplete-report-budget") continue;

            var path = entry.NormalizedPath;
            if (!Consume(Math.Max(1, path.Length))) return new(findings, false, reasons);
            if (path.Length > 0)
            {
                var fullNode = GetNode(path);
                fullNode.ExactCount++;
                if (!entry.IsDirectory) fullNode.FileCount++;
                else fullNode.ExplicitDirectoryCount++;
                fullNode.AddVariant(path);
                fullNode.AddSample(entry.OriginalName, budget.MaxFindingParticipants);

                var slash = path.IndexOf('/');
                while (slash >= 0)
                {
                    var parent = path[..slash];
                    if (!Consume(Math.Max(1, parent.Length))) return new(findings, false, reasons);
                    if (parent.Length > 0)
                    {
                        var parentNode = GetNode(parent);
                        parentNode.HasChildren = true;
                        parentNode.AddVariant(parent);
                        parentNode.AddSample(entry.OriginalName, budget.MaxFindingParticipants);
                    }
                    slash = path.IndexOf('/', slash + 1);
                }
            }

            if (!AddPathHazards(entry, path, GetNode(path)))
                return new(findings, false, reasons);
        }

        foreach (var (path, node) in nodes)
        {
            if (!Consume()) return new(findings, false, reasons);
            if (node.ExactCount > 1 && !AddFinding("duplicate-normalized-path", path, node,
                    "Multiple entries normalize to the same path."))
                return new(findings, false, reasons);
            if (node.Variants.Count > 1 && !AddFinding("case-insensitive-collision", path, node,
                    "Distinct full or implicit directory paths collide under case-insensitive comparison."))
                return new(findings, false, reasons);
            if (node.FileCount > 0 && (node.ExplicitDirectoryCount > 0 || node.HasChildren)
                && !AddFinding("file-directory-conflict", path, node,
                    "A path is used as a file and also as an explicit or implicit directory."))
                return new(findings, false, reasons);
            if (node.ExplicitDirectoryCount > 1 && !AddFinding("duplicate-explicit-directory", path, node,
                    "The archive declares the same directory path more than once."))
                return new(findings, false, reasons);
            if (node.ExplicitDirectoryCount > 0 && node.HasChildren
                && !AddFinding("explicit-and-implicit-directory", path, node,
                    "A directory is explicitly declared and also implied by child entries; this is not inherently a conflict."))
                return new(findings, false, reasons);
        }

        return new(findings, complete, reasons);

        PathNode GetNode(string key)
        {
            if (!nodes.TryGetValue(key, out var node)) nodes[key] = node = new PathNode();
            return node;
        }

        bool AddPathHazards(EntryMeasurement entry, string normalized, PathNode node)
        {
            var path = entry.OriginalName.Replace('\\', '/');
            if (string.IsNullOrEmpty(path) || path.StartsWith('/')
                || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'))
            {
                if (!AddFinding("unsafe-path", normalized, node, "Entry is empty, absolute, UNC, or drive-qualified.")) return false;
            }
            if (path.Split('/').Any(segment => segment == ".."))
                if (!AddFinding("traversal-path", normalized, node, "Entry contains a parent-directory segment.")) return false;
            if (path.Contains('\0') || path.Any(char.IsControl))
                if (!AddFinding("invalid-path", normalized, node, "Entry contains a NUL or control character.")) return false;

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => segment.Contains(':') || segment.EndsWith('.') || segment.EndsWith(' ')
                || segment.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0 || IsReservedWindowsName(segment)))
            {
                if (!AddFinding("windows-ambiguous-path", normalized, node,
                        "Entry contains a Windows-invalid character, reserved device name, colon, or segment ending in dot/space.")) return false;
            }
            if (normalized.Length == 0)
                if (!AddFinding("empty-normalized-path", normalized, node, "Entry normalizes to an empty path.")) return false;
            return true;
        }
    }

    public static (bool Extract, string Reason) ApplyFilter(ExtractionRole role, string normalizedPath, bool isDirectory)
    {
        if (isDirectory) return (false, "Directory entry; the Java extractor writes files only.");

        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (role == ExtractionRole.RuntimeApk)
        {
            var parentName = segments.Length >= 2 ? segments[^2] : null;
            var grandparentName = segments.Length >= 3 ? segments[^3] : null;
            if (string.Equals(parentName, "res", StringComparison.Ordinal)
                || string.Equals(grandparentName, "res", StringComparison.Ordinal))
                return (false, "Skipped by the existing runtime APK res filter.");
            return (true, "Accepted by the existing runtime APK filter.");
        }

        if (role == ExtractionRole.ExternalRuntimeZpak)
        {
            if (segments.Length > 0 && string.Equals(segments[^1], "image.ps", StringComparison.Ordinal))
                return (false, "Skipped by the existing external ZPAK image.ps filter.");
            if (segments.Length >= 2 && string.Equals(segments[^2], "manual", StringComparison.Ordinal))
                return (false, "Skipped by the existing external ZPAK manual-directory filter.");
        }

        return (true, role == ExtractionRole.EmbeddedDefaultZpak
            ? "Accepted; embedded default ZPAK has no entry filter."
            : "Accepted by the existing external ZPAK filter.");
    }

    private static bool IsReservedWindowsName(string segment)
    {
        var baseName = segment.Split('.')[0];
        if (baseName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("CONIN$", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase)) return true;
        return baseName.Length == 4
            && (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && (baseName[3] is >= '1' and <= '9' || baseName[3] is '\u00b9' or '\u00b2' or '\u00b3');
    }

    private sealed class PathNode
    {
        private readonly HashSet<string> _variants = new(StringComparer.Ordinal);
        private readonly List<string> _samples = [];
        private bool _samplesTruncated;
        public int ExactCount { get; set; }
        public int FileCount { get; set; }
        public int ExplicitDirectoryCount { get; set; }
        public bool HasChildren { get; set; }
        public IReadOnlyCollection<string> Variants => _variants;
        public void AddVariant(string value) => _variants.Add(value);
        public void AddSample(string value, int limit)
        {
            if (_samples.Contains(value, StringComparer.Ordinal)) return;
            if (_samples.Count < limit) _samples.Add(value);
            else _samplesTruncated = true;
        }
        public IReadOnlyList<string> GetSamples(int limit, out bool truncated)
        {
            truncated = _samplesTruncated || _samples.Count > limit;
            return _samples.Take(limit).ToArray();
        }
    }
}
