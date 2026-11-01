namespace PSXperia.Engine;
public enum ConversionStage { Queued, ValidatingInput, PreparingRuntime, ConvertingImage, BuildingPackage, ValidatingOutput, Completed, Failed }
public sealed record ConversionProgress(ConversionStage Stage, int Percent, string Message);
public sealed record ConversionRequest(string ImagePath, string OutputDirectory, string TitleId, string GameName, string? CoverImagePath = null, RuntimeSelection? Runtime = null);
public interface IConversionEngine
{
    Task ValidateInputAsync(string imagePath, CancellationToken cancellationToken = default);
    Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default);
}
