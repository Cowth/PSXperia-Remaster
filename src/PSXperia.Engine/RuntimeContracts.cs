namespace PSXperia.Engine;

public sealed record RuntimeSelection(string ApkPath, string ZpakPath, string? ProfileName = null);
public sealed record RuntimeValidationResult(bool IsValid, string? TitleId, string? RuntimeVersion,
    string? ApkSha256, string? ApkCrc32, IReadOnlyList<string> Checks, IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public interface IRuntimeValidator
{
    Task<RuntimeValidationResult> ValidateAsync(RuntimeSelection selection, CancellationToken cancellationToken = default);
}
