using PSXperia.Engine;
using System.Text;

namespace PSXperia.Engine.Tests;

public sealed class DiscSerialScannerTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void FindSerial_FindsSerialInSyntheticDataTrack()
    {
        using var workspace = new TestWorkspace();
        var dataPath = workspace.WriteFile("data.bin", Encoding.ASCII.GetBytes("\0\0 SLUS_123.45\0"));
        var inspection = InspectionWithTracks(dataPath, "unused-audio.bin");

        Assert.Equal("SLUS-123.45", DiscSerialScanner.FindSerial(inspection));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FindSerial_DoesNotScanAudioOnlyFilesWhenCueTracksAreAvailable()
    {
        using var workspace = new TestWorkspace();
        var audioPath = workspace.WriteFile("audio.bin", Encoding.ASCII.GetBytes("\0 SCUS_54321\0"));
        var inspection = new DiscImageInspection("disc.cue", "CUE", 32, null, [audioPath],
            [new DiscTrack(1, audioPath, DiscTrackMode.Audio, null, "00:00:00")], []);

        Assert.Null(DiscSerialScanner.FindSerial(inspection));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FindSerial_DetectsSerialSplitAcrossReadBuffers()
    {
        using var workspace = new TestWorkspace();
        // Keep this boundary aligned to DiscSerialScanner's actual 1 MiB + 64 byte read buffer.
        const int scannerReadBufferSize = 1024 * 1024 + 64;
        const int serialStart = scannerReadBufferSize - 5;
        const string serial = "SCES_12345";
        Assert.True(serialStart < scannerReadBufferSize && serialStart + serial.Length > scannerReadBufferSize);

        var bytes = new byte[scannerReadBufferSize + 64];
        Array.Fill(bytes, (byte)0);
        Encoding.ASCII.GetBytes(serial).CopyTo(bytes, serialStart);
        var dataPath = workspace.WriteFile("boundary.bin", bytes);
        var inspection = new DiscImageInspection(dataPath, "BIN", bytes.Length, null, [dataPath], [], []);

        Assert.Equal("SCES-12345", DiscSerialScanner.FindSerial(inspection));
    }

    private static DiscImageInspection InspectionWithTracks(string dataPath, string audioPath) =>
        new("disc.cue", "CUE", 64, null, [dataPath, audioPath],
        [
            new DiscTrack(1, dataPath, DiscTrackMode.Mode1_2352, null, "00:00:00"),
            new DiscTrack(2, audioPath, DiscTrackMode.Audio, null, "00:12:00")
        ], []);
}
