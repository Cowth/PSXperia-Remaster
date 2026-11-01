using PSXperia.Engine;

namespace PSXperia.Engine.Tests;

public sealed class DiscImageInspectorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void InspectCue_ParsesTrackModesIndexesAndReferencedFiles()
    {
        using var workspace = new TestWorkspace();
        var data = workspace.WriteFile("disc data.bin", [1, 2, 3, 4]);
        var audio = workspace.WriteFile("audio.bin", [5, 6, 7]);
        var cue = workspace.WriteText("disc.cue", """
            FILE "disc data.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 00 00:10:00
                INDEX 01 00:12:00
            FILE "audio.bin" BINARY
              TRACK 03 AUDIO
                INDEX 01 00:00:00
            """);

        var inspection = DiscImageInspector.Inspect(cue);

        Assert.Equal("CUE", inspection.Format);
        Assert.Equal(2, inspection.DataFiles.Count);
        Assert.Equal(new[] { data, audio }, inspection.DataFiles);
        Assert.Equal(3, inspection.Tracks.Count);
        Assert.Equal(1, inspection.DataTrackCount);
        Assert.Equal(2, inspection.AudioTrackCount);
        Assert.Equal(DiscTrackMode.Mode1_2352, inspection.Tracks[0].Mode);
        Assert.Equal("00:00:00", inspection.Tracks[0].Index01);
        Assert.Equal("00:10:00", inspection.Tracks[1].Index00);
        Assert.Equal("00:12:00", inspection.Tracks[1].Index01);
        Assert.Equal(3, inspection.Tracks[2].Number);
        Assert.Equal(audio, inspection.Tracks[2].FilePath);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void InspectCue_ReportsUnknownModeAndMissingIndex()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("track.bin", [1]);
        var cue = workspace.WriteText("warnings.cue", "FILE \"track.bin\" BINARY\nTRACK 01 MODE9/9999\n");

        var inspection = DiscImageInspector.Inspect(cue);

        Assert.Equal(DiscTrackMode.Unknown, inspection.Tracks[0].Mode);
        Assert.Contains(inspection.Warnings, warning => warning.Contains("unrecognized", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(inspection.Warnings, warning => warning.Contains("INDEX 01", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void InspectCue_RejectsMissingTrackFileAndTrackBeforeFile()
    {
        using var workspace = new TestWorkspace();
        var missingTrack = workspace.WriteText("missing.cue", "FILE \"not-here.bin\" BINARY\nTRACK 01 MODE1/2352\nINDEX 01 00:00:00\n");
        var trackBeforeFile = workspace.WriteText("invalid.cue", "TRACK 01 MODE1/2352\n");

        Assert.Throws<FileNotFoundException>(() => DiscImageInspector.Inspect(missingTrack));
        Assert.Throws<InvalidDataException>(() => DiscImageInspector.Inspect(trackBeforeFile));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void InspectRaw_ReportsSectorCountsForSyntheticImages()
    {
        using var workspace = new TestWorkspace();
        var iso = workspace.WriteFile("sample.iso", new byte[4096]);
        var rawBin = workspace.WriteFile("sample.bin", new byte[2352 * 2]);

        Assert.Equal(2, DiscImageInspector.Inspect(iso).SectorCount);
        Assert.Equal(2, DiscImageInspector.Inspect(rawBin).SectorCount);
    }
}
