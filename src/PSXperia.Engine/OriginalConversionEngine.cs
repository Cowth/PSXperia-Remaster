using System.IO.Compression;

namespace PSXperia.Engine;

/// <summary>First conversion stage: validates source/runtime and creates original-format PSImage, TOC, and game ZPAK payload.</summary>
public sealed class OriginalConversionEngine(IRuntimeValidator runtimeValidator) : IConversionEngine
{
    public Task ValidateInputAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(imagePath)) throw new FileNotFoundException("Disc image not found.", imagePath);
        var ext = Path.GetExtension(imagePath);
        if (!new[] { ".cue", ".iso", ".bin", ".img" }.Contains(ext, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a CUE sheet, data-track BIN/IMG, or ISO image.");
        var sourcePath = ResolveDataImagePath(imagePath);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The selected data-track image was not found.", sourcePath);
        if (new FileInfo(sourcePath).Length == 0) throw new InvalidDataException("Disc image is empty.");
        return Task.CompletedTask;
    }

    public async Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await ValidateInputAsync(request.ImagePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.OutputDirectory)) throw new ArgumentException("Choose an output directory.");
        if (string.IsNullOrWhiteSpace(request.TitleId) || request.TitleId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("A valid Sony title ID is required.");

        progress?.Report(new(ConversionStage.ValidatingInput, 2, "Checking source image"));
        if (request.Runtime is null) throw new InvalidOperationException("Select your legally obtained Crash Bandicoot APK and matching ZPAK in Runtime Manager first.");
        progress?.Report(new(ConversionStage.PreparingRuntime, 4, "Validating selected Sony runtime"));
        var runtime = await runtimeValidator.ValidateAsync(request.Runtime, cancellationToken);
        if (!runtime.IsValid) throw new InvalidDataException("Runtime validation failed:\n" + string.Join("\n", runtime.Errors));
        // Do not compare the Crash Bandicoot runtime title ID to the converted game's output ID:
        // the original workflow deliberately uses a distinct title ID for each generated game.
        // The validator already checks that the selected APK and standalone runtime ZPAK match each other.
        // The runtime is validated but not yet patched into an installable APK in this milestone.
        var workspace = Path.Combine(Path.GetFullPath(request.OutputDirectory), "PSXperia-" + Sanitize(request.GameName));
        if (Directory.Exists(workspace)) throw new IOException($"Output already exists: {workspace}");
        var zpakRoot = Path.Combine(workspace, "ZPAK");
        Directory.CreateDirectory(Path.Combine(zpakRoot, "data"));
        try
        {
            progress?.Report(new(ConversionStage.ConvertingImage, 5, "Writing original PSXperia image container"));
            var dataImagePath = ResolveDataImagePath(request.ImagePath);
            await OriginalPsImageWriter.WriteAsync(dataImagePath,
                Path.Combine(zpakRoot, "data", "image.ps"), Path.Combine(workspace, "image_ps_toc.bin"), progress, cancellationToken);
            progress?.Report(new(ConversionStage.BuildingPackage, 92, "Creating original-format game ZPAK"));
            var gameZpak = Path.Combine(workspace, request.TitleId + ".zpak");
            await OriginalZpakWriter.CreateStoredAsync(zpakRoot, gameZpak, cancellationToken);
            progress?.Report(new(ConversionStage.ValidatingOutput, 97, "Checking generated files"));
            using (var archive = ZipFile.OpenRead(gameZpak))
            {
                if (archive.GetEntry("data/image.ps") is null) throw new InvalidDataException("Generated ZPAK is missing data/image.ps.");
            }
            if (!File.Exists(Path.Combine(workspace, "image_ps_toc.bin"))) throw new InvalidDataException("Generated TOC is missing.");
            progress?.Report(new(ConversionStage.Completed, 100, "Image payload and game ZPAK created. APK integration is the next stage."));
        }
        catch
        {
            try { if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true); } catch { /* preserve original exception */ }
            throw;
        }
    }

    private static string ResolveDataImagePath(string imagePath)
    {
        if (Path.GetExtension(imagePath).Equals(".cue", StringComparison.OrdinalIgnoreCase))
        {
            var inspection = DiscImageInspector.Inspect(imagePath);
            var dataTrack = inspection.Tracks.FirstOrDefault(t => t.Mode != DiscTrackMode.Audio);
            if (dataTrack is null)
                throw new InvalidDataException("The CUE sheet does not contain a readable data track. Audio-only discs cannot be converted.");
            return dataTrack.FilePath;
        }
        return Path.GetFullPath(imagePath);
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "Game" : clean;
    }
}
