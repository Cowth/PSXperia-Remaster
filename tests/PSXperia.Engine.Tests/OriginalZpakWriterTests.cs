using PSXperia.Engine;
using System.IO.Compression;

namespace PSXperia.Engine.Tests;

/// <summary>
/// Tests the standalone prototype C# ZPAK writer. Passing these tests does not
/// establish compatibility with the active Java PSXperia conversion pipeline.
/// </summary>
public sealed class OriginalZpakWriterTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateStoredAsync_WritesSortedForwardSlashEntriesAndOmitsHiddenPaths()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.PathFor("source");
        Directory.CreateDirectory(source);
        workspace.WriteFile("source/data/image.ps", [0x50, 0x53, 0x49, 0x4D]);
        workspace.WriteText("source/metadata.xml", "<metadata>synthetic</metadata>");
        workspace.WriteText("source/.hidden", "not included");
        workspace.WriteText("source/.private/secret.txt", "not included");
        workspace.WriteText("source/data/.ignored", "not included");
        var output = workspace.PathFor("package/test.zpak");

        await OriginalZpakWriter.CreateStoredAsync(source, output);

        using var archive = ZipFile.OpenRead(output);
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Equal(new[] { "data/image.ps", "metadata.xml" }, names);
        foreach (var entry in archive.Entries)
        {
            Assert.DoesNotContain('\\', entry.FullName);
            Assert.Equal(entry.Length, entry.CompressedLength);
        }
        using var imageStream = archive.GetEntry("data/image.ps")!.Open();
        using var bytes = new MemoryStream();
        await imageStream.CopyToAsync(bytes);
        Assert.Equal(new byte[] { 0x50, 0x53, 0x49, 0x4D }, bytes.ToArray());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateStoredAsync_RejectsMissingSourceDirectory()
    {
        using var workspace = new TestWorkspace();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            OriginalZpakWriter.CreateStoredAsync(workspace.PathFor("missing"), workspace.PathFor("out.zpak")));
    }
}
