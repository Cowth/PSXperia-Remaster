using System.IO.Compression;

namespace PSXperia.Engine;

/// <summary>Creates the original PSXperia ZPAK ZIP container with relative forward-slash entry paths.</summary>
public static class OriginalZpakWriter
{
    public static async Task CreateStoredAsync(string sourceDirectory, string outputPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);
        var root = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => !Path.GetRelativePath(root, p).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(s => s.StartsWith('.')))
            .OrderBy(p => Path.GetRelativePath(root, p).Replace('\\', '/'), StringComparer.Ordinal).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 128 * 1024, true);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryName = Path.GetRelativePath(root, file).Replace('\\', '/');
            var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
            await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
            await using var target = entry.Open();
            await source.CopyToAsync(target, cancellationToken);
        }
    }
}
