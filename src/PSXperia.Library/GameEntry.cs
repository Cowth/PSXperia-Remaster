namespace PSXperia.Library;

public enum CompatibilityStatus { Untested, Boots, Playable, Verified, Issues, Unplayable }

public sealed record GameEntry(
    Guid Id,
    string Title,
    string? TitleId,
    string ImagePath,
    string? CoverPath,
    CompatibilityStatus Compatibility,
    DateTime DateAdded)
{
    public string? Region { get; init; }
    public string? DiscSerial { get; init; }
    public string? MetadataSource { get; init; }
    public string? CoverSource { get; init; }
    public bool CoverIsManual { get; init; }
    public int TrackCount { get; init; }
    public int DataTrackCount { get; init; }
    public int AudioTrackCount { get; init; }
    public IReadOnlyList<GameTrackEntry> Tracks { get; init; } = Array.Empty<GameTrackEntry>();
}

public sealed record GameTrackEntry(
    int Number,
    string FilePath,
    string Mode,
    string? Index00,
    string? Index01);
