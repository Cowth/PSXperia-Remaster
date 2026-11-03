using System.IO.Compression;
using System.Text;

namespace PSXperia.RuntimeArchiveMeasure;

internal static class SyntheticDemo
{
    public static async Task<MeasurementReport> RunAsync(ScanBudget? budget = null, CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(Path.GetTempPath(), $"PSXperiaArchiveMeasureDemo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        MeasurementReport? report = null;
        var cleanupStatus = "complete";
        try
        {
            var embedded = CreateZip([
                ("metadata.xml", "<metadata>synthetic placeholder</metadata>"),
                ("assets/default/icon.png", "synthetic placeholder bytes")]);
            var apk = Path.Combine(root, "synthetic-runtime.apk");
            WriteZip(apk, [
                ("AndroidManifest.xml", Encoding.UTF8.GetBytes("synthetic placeholder")),
                ("classes.dex", Encoding.UTF8.GetBytes("synthetic placeholder")),
                ("res/layout/example.xml", Encoding.UTF8.GetBytes("synthetic placeholder")),
                ("assets/default-runtime.zpak", embedded),
                ("assets/ordinary.dat", Encoding.UTF8.GetBytes("synthetic placeholder"))]);

            var zpak = Path.Combine(root, "synthetic-runtime.zpak");
            WriteZip(zpak, [
                ("metadata.xml", "<metadata>synthetic placeholder</metadata>"),
                ("data/image.ps", "synthetic placeholder"),
                ("data/ps1_rom.bin", "synthetic placeholder"),
                ("manual/readme.txt", "synthetic placeholder"),
                ("assets/other.dat", "synthetic placeholder")]);

            report = await new ArchiveMeasurementService().MeasureAsync(apk, zpak, budget, cancellationToken);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { cleanupStatus = $"incomplete: synthetic demo files could not be removed: {ex.Message}"; }
        }
        var scanCleanup = report!.TemporaryStagingCleanupStatus;
        var combinedCleanup = scanCleanup == "complete" ? cleanupStatus
            : cleanupStatus == "complete" ? scanCleanup : $"{scanCleanup}; {cleanupStatus}";
        return report with { TemporaryStagingCleanupStatus = combinedCleanup };
    }

    private static byte[] CreateZip(IEnumerable<(string Name, string Content)> entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
                writer.Write(content);
            }
        return memory.ToArray();
    }

    private static void WriteZip(string path, IEnumerable<(string Name, string Content)> entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }

    private static void WriteZip(string path, IEnumerable<(string Name, byte[] Content)> entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
            using (var stream = zip.CreateEntry(name).Open()) stream.Write(content);
    }
}
