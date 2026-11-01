using PSXperia.Engine;
using System.Buffers.Binary;
using System.IO.Compression;

namespace PSXperia.Engine.Tests;

/// <summary>
/// Tests the standalone prototype C# PSImage/TOC writer. Passing these tests does not
/// establish compatibility with the active Java PSXperia conversion pipeline.
/// </summary>
public sealed class OriginalPsImageWriterTests
{
    public static TheoryData<int> SegmentLengths => new()
    {
        1,
        OriginalPsImageWriter.PartSize - 1,
        OriginalPsImageWriter.PartSize,
        OriginalPsImageWriter.PartSize + 1,
        OriginalPsImageWriter.PartSize * 2 + 17
    };

    [Theory]
    [MemberData(nameof(SegmentLengths))]
    [Trait("Category", "Unit")]
    public async Task WriteAsync_WritesHeaderSegmentsAndLittleEndianToc(int length)
    {
        using var workspace = new TestWorkspace();
        var inputBytes = Enumerable.Range(0, length).Select(i => (byte)((i * 31 + length) % 251)).ToArray();
        var input = workspace.WriteFile("input.iso", inputBytes);
        var imagePath = workspace.PathFor("output/image.ps");
        var tocPath = workspace.PathFor("output/image_ps_toc.bin");

        await OriginalPsImageWriter.WriteAsync(input, imagePath, tocPath);

        var image = await File.ReadAllBytesAsync(imagePath);
        var toc = await File.ReadAllBytesAsync(tocPath);
        byte[] expectedHeader = [0x07, 0x70, 0xFE, 0xDA, 0x03, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        byte[] expectedTocConstant = [0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00];
        Assert.Equal(expectedHeader, image[..expectedHeader.Length]);

        var partCount = (length + OriginalPsImageWriter.PartSize - 1) / OriginalPsImageWriter.PartSize;
        Assert.Equal(0x18 + partCount * 4, toc.Length);
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32LittleEndian(toc.AsSpan(0, 4)));
        Assert.Equal((uint)length, BinaryPrimitives.ReadUInt32LittleEndian(toc.AsSpan(4, 4)));
        Assert.Equal(expectedTocConstant, toc[8..20]);
        Assert.Equal((uint)partCount, BinaryPrimitives.ReadUInt32LittleEndian(toc.AsSpan(0x14, 4)));

        var sourceOffset = 0;
        for (var part = 0; part < partCount; part++)
        {
            var compressedOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(toc.AsSpan(0x18 + part * 4, 4)));
            var nextOffset = part + 1 < partCount
                ? checked((int)BinaryPrimitives.ReadUInt32LittleEndian(toc.AsSpan(0x18 + (part + 1) * 4, 4)))
                : image.Length;
            Assert.InRange(compressedOffset, expectedHeader.Length, image.Length - 1);
            Assert.True(nextOffset > compressedOffset);
            using var compressed = new MemoryStream(image, compressedOffset, nextOffset - compressedOffset, writable: false);
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            await zlib.CopyToAsync(decompressed);

            var expectedLength = Math.Min(OriginalPsImageWriter.PartSize, length - sourceOffset);
            Assert.Equal(inputBytes.AsSpan(sourceOffset, expectedLength).ToArray(), decompressed.ToArray());
            sourceOffset += expectedLength;
        }
        Assert.Equal(length, sourceOffset);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task WriteAsync_RejectsMissingAndEmptyInputs()
    {
        using var workspace = new TestWorkspace();
        var missing = workspace.PathFor("missing.iso");
        var empty = workspace.WriteFile("empty.iso", []);

        await Assert.ThrowsAsync<InvalidDataException>(() => OriginalPsImageWriter.WriteAsync(missing,
            workspace.PathFor("missing-out/image.ps"), workspace.PathFor("missing-out/toc.bin")));
        await Assert.ThrowsAsync<InvalidDataException>(() => OriginalPsImageWriter.WriteAsync(empty,
            workspace.PathFor("empty-out/image.ps"), workspace.PathFor("empty-out/toc.bin")));
        Assert.False(File.Exists(workspace.PathFor("empty-out/image.ps")));
    }
}
