using PSXperia.Engine;
using System.IO.Compression;
using System.Text;

namespace PSXperia.Engine.Tests;

/// <summary>
/// These synthetic archives use placeholder manifest and ELF entry bytes. Tests cover
/// archive-entry and metadata validation only; they do not establish valid Android APK
/// structure or Sony runtime compatibility.
/// </summary>
public sealed class RuntimeValidatorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_SyntheticArchivesPassStructureButAreRejectedAsUnsupportedRuntime()
    {
        using var workspace = new TestWorkspace();
        var selection = CreateSyntheticSelection(workspace);

        var result = await new RuntimeValidator().ValidateAsync(selection);

        Assert.False(result.IsValid);
        Assert.Contains(result.Checks, check => check.Contains("required emulator files", StringComparison.Ordinal));
        Assert.Contains(result.Checks, check => check.Contains("Embedded runtime metadata found", StringComparison.Ordinal));
        Assert.Contains(result.Checks, check => check.Contains("External ZPAK metadata parsed", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.StartsWith("APK CRC32 ", StringComparison.Ordinal));
        Assert.Equal("CB_TEST", result.TitleId);
        Assert.Equal("1.0-test", result.RuntimeVersion);
        Assert.False(string.IsNullOrWhiteSpace(result.ApkSha256));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_ReportsMissingApkEntries()
    {
        using var workspace = new TestWorkspace();
        var apk = WriteZip(workspace, "incomplete.apk", [("classes.dex", [1, 2])]);
        var zpak = WriteRuntimeZpak(workspace, "runtime.zpak", "CB_TEST");

        var result = await new RuntimeValidator().ValidateAsync(new RuntimeSelection(apk, zpak));

        Assert.Contains(result.Errors, error => error.Contains("AndroidManifest.xml", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("lib/armeabi/libjava-activity.so", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_ReportsApkAndZpakTitleIdMismatch()
    {
        using var workspace = new TestWorkspace();
        var apk = WriteSyntheticApk(workspace, "runtime.apk", "CB_ONE");
        var zpak = WriteRuntimeZpak(workspace, "runtime.zpak", "CB_TWO");

        var result = await new RuntimeValidator().ValidateAsync(new RuntimeSelection(apk, zpak));

        Assert.Contains(result.Errors, error => error.Contains("title ID mismatch", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.StartsWith("APK CRC32 ", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_ReportsMalformedApkAndMalformedExternalZpak()
    {
        using var workspace = new TestWorkspace();
        var apk = workspace.WriteText("broken.apk", "not a zip archive");
        var zpak = workspace.WriteText("broken.zpak", "not a zip archive");

        var result = await new RuntimeValidator().ValidateAsync(new RuntimeSelection(apk, zpak));

        Assert.Contains(result.Errors, error => error.StartsWith("APK validation failed:", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.StartsWith("ZPAK validation failed:", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_ReportsMissingFilesWithoutThrowing()
    {
        using var workspace = new TestWorkspace();

        var result = await new RuntimeValidator().ValidateAsync(
            new RuntimeSelection(workspace.PathFor("missing.apk"), workspace.PathFor("missing.zpak")));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("APK does not exist", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("ZPAK does not exist", StringComparison.Ordinal));
    }

    internal static string WriteSyntheticApk(TestWorkspace workspace, string name, string titleId)
    {
        // Manifest, DEX, resource, and ELF entry payloads below are placeholders used only
        // to exercise ZIP entry-name and metadata checks; this is not a valid Android APK.
        var nestedZpak = CreateZipBytes(
        [
            ("metadata.xml", Encoding.UTF8.GetBytes($"<metadata version=\"1.0-test\"><title_id>{titleId}</title_id></metadata>")),
            ("data/image.ps", [0x50, 0x53, 0x49, 0x4D])
        ]);
        return WriteZip(workspace, name,
        [
            ("AndroidManifest.xml", [0x41, 0x4E, 0x44, 0x52]),
            ("classes.dex", [0x64, 0x65, 0x78]),
            ("resources.arsc", [0x52, 0x53, 0x52, 0x43]),
            ("lib/armeabi/libjava-activity.so", [0x7F, 0x45, 0x4C, 0x46]),
            ("lib/armeabi/libzplatform.so", [0x7F, 0x45, 0x4C, 0x46]),
            ("assets/default.zpak", nestedZpak)
        ]);
    }

    internal static string WriteRuntimeZpak(TestWorkspace workspace, string name, string titleId) =>
        WriteZip(workspace, name,
        [
            ("metadata.xml", Encoding.UTF8.GetBytes($"<metadata><title_id>{titleId}</title_id><dependencies><title_version>1.0-test</title_version></dependencies></metadata>")),
            ("data/image.ps", [0x50, 0x53, 0x49, 0x4D])
        ]);

    private static RuntimeSelection CreateSyntheticSelection(TestWorkspace workspace) =>
        new(WriteSyntheticApk(workspace, "runtime.apk", "CB_TEST"), WriteRuntimeZpak(workspace, "runtime.zpak", "CB_TEST"));

    private static string WriteZip(TestWorkspace workspace, string name, IReadOnlyList<(string Name, byte[] Bytes)> entries)
    {
        var path = workspace.PathFor(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (entryName, bytes) in entries)
        {
            var entry = archive.CreateEntry(entryName);
            using var target = entry.Open();
            target.Write(bytes);
        }
        return path;
    }

    private static byte[] CreateZipBytes(IReadOnlyList<(string Name, byte[] Bytes)> entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryName, bytes) in entries)
            {
                var entry = archive.CreateEntry(entryName);
                using var target = entry.Open();
                target.Write(bytes);
            }
        }
        return stream.ToArray();
    }
}
