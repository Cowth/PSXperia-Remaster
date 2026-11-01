using System.Buffers.Binary;
using System.IO.Compression;

namespace PSXperia.Engine;

/// <summary>Writes the original PSXperia PSImage container: 0x9300-byte independent zlib segments plus its LE TOC.</summary>
public static class OriginalPsImageWriter
{
    public const int PartSize = 0x9300;
    private static readonly byte[] Header = [0x07, 0x70, 0xFE, 0xDA, 0x03, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
    private static readonly byte[] TocConstant = [0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00];

    public static async Task WriteAsync(string inputPath, string imagePsPath, string tocPath,
        IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var inputInfo = new FileInfo(inputPath);
        if (!inputInfo.Exists || inputInfo.Length == 0) throw new InvalidDataException("The disc image is missing or empty.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(imagePsPath))!);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(tocPath))!);
        var offsets = new List<uint>(); long readTotal = 0;
        await using (var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 0x700, true))
        await using (var output = new FileStream(imagePsPath, FileMode.Create, FileAccess.Write, FileShare.None, 0x700, true))
        {
            await output.WriteAsync(Header, cancellationToken);
            var buffer = new byte[PartSize];
            while (true)
            {
                var filled = 0;
                while (filled < buffer.Length)
                {
                    var n = await input.ReadAsync(buffer.AsMemory(filled, buffer.Length - filled), cancellationToken);
                    if (n == 0) break;
                    filled += n; readTotal += n;
                }
                if (filled == 0) break;
                if (output.Position > uint.MaxValue) throw new InvalidDataException("PSImage output exceeds the original 32-bit TOC offset limit.");
                offsets.Add(checked((uint)output.Position));
                // Java DeflaterOutputStream writes a zlib-wrapped DEFLATE stream; ZLibStream is the .NET equivalent.
                await using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
                    await zlib.WriteAsync(buffer.AsMemory(0, filled), cancellationToken);
                var percent = (int)Math.Min(99, readTotal * 100 / inputInfo.Length);
                progress?.Report(new(ConversionStage.ConvertingImage, percent, $"Compressed {readTotal:N0} / {inputInfo.Length:N0} bytes"));
                if (filled < buffer.Length) break;
            }
            await output.FlushAsync(cancellationToken);
        }

        var toc = new byte[checked(0x18 + offsets.Count * 4)];
        BinaryPrimitives.WriteUInt32LittleEndian(toc.AsSpan(0, 4), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(toc.AsSpan(4, 4), checked((uint)readTotal));
        TocConstant.CopyTo(toc, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(toc.AsSpan(0x14, 4), checked((uint)offsets.Count));
        for (var i = 0; i < offsets.Count; i++) BinaryPrimitives.WriteUInt32LittleEndian(toc.AsSpan(0x18 + i * 4, 4), offsets[i]);
        await File.WriteAllBytesAsync(tocPath, toc, cancellationToken);
        progress?.Report(new(ConversionStage.ConvertingImage, 100, $"PSImage created with {offsets.Count} segments"));
    }
}
