using System.Diagnostics;
using System.IO.Compression;

namespace PSXperia.Engine;

/// <summary>
/// Runs the original PSXperia command-line converter as the compatibility backend.
/// This deliberately delegates APK decoding, patch selection, wrapper integration,
/// AAPT packaging, and signing to the original implementation instead of approximating them.
/// </summary>
public sealed class OriginalPsxperiaCliEngine(IRuntimeValidator runtimeValidator) : IConversionEngine
{
    private const string MainClass = "com.yifanlu.PSXperiaTool.Interface.CommandLine";
    private static readonly string[] SupportedRuntimeCrcs = ["76C201D8", "7C33475B", "BB542581", "E7BCB6D5"];

    public Task ValidateInputAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(imagePath)) throw new FileNotFoundException("Disc image not found.", imagePath);
        var ext = Path.GetExtension(imagePath);
        if (!new[] { ".cue", ".iso", ".bin", ".img" }.Contains(ext, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a CUE sheet, data-track BIN/IMG, or ISO image.");
        var source = ResolveDataImagePath(imagePath);
        if (!File.Exists(source)) throw new FileNotFoundException("The selected data-track image was not found.", source);
        if (new FileInfo(source).Length == 0) throw new InvalidDataException("Disc image is empty.");
        return Task.CompletedTask;
    }

    public async Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await ValidateInputAsync(request.ImagePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.OutputDirectory) || !Directory.Exists(request.OutputDirectory))
            throw new DirectoryNotFoundException("Choose an existing output directory.");
        if (!IsValidTitleId(request.TitleId))
            throw new ArgumentException("Title ID must contain only letters, numbers, and underscores (for example SLUS00128_1).");
        if (request.Runtime is null) throw new InvalidOperationException("Select your legally obtained Crash Bandicoot APK and matching runtime ZPAK first.");

        progress?.Report(new(ConversionStage.ValidatingInput, 2, "Checking disc image and title ID"));
        var validation = await runtimeValidator.ValidateAsync(request.Runtime, cancellationToken);
        if (!validation.IsValid)
            throw new InvalidDataException("Runtime validation failed:\n" + string.Join("\n", validation.Errors));
        if (validation.ApkCrc32 is null || !SupportedRuntimeCrcs.Contains(validation.ApkCrc32, StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException($"The original PSXperia patch set does not support APK CRC32 {validation.ApkCrc32 ?? "(unknown)"}. No output was created.");

        var java = ResolveJavaExecutable();
        var jar = Path.Combine(AppContext.BaseDirectory, "tools", "PSXperiaTool.jar");
        if (!File.Exists(jar)) throw new FileNotFoundException("The original PSXperia converter engine is missing from tools/PSXperiaTool.jar.", jar);

        var workRoot = Path.Combine(Path.GetTempPath(), "PSXperiaRemastered", Guid.NewGuid().ToString("N"));
        var extractedRuntime = Path.Combine(workRoot, "runtime-data");
        var stagedOutput = Path.Combine(workRoot, "final-output");
        Directory.CreateDirectory(workRoot);
        try
        {
            progress?.Report(new(ConversionStage.PreparingRuntime, 5, $"Runtime profile {validation.ApkCrc32} confirmed; extracting original Sony runtime"));
            await RunJavaAsync(java, jar, workRoot,
                ["extract", Path.GetFullPath(request.Runtime.ApkPath), Path.GetFullPath(request.Runtime.ZpakPath), extractedRuntime],
                cancellationToken, progress, 8, 18);

            progress?.Report(new(ConversionStage.ConvertingImage, 20, "Running the original PSXperia image converter and game-specific patch stage"));
            var sourceImage = ResolveDataImagePath(request.ImagePath);
            var args = new List<string> { "convert", "-D", extractedRuntime, "--game-name", request.GameName };
            if (!string.IsNullOrWhiteSpace(request.CoverImagePath) && File.Exists(request.CoverImagePath))
            {
                args.Add("--icon-file"); args.Add(Path.GetFullPath(request.CoverImagePath));
            }
            args.Add(request.TitleId);
            args.Add(sourceImage);
            args.Add(stagedOutput);
            await RunJavaAsync(java, jar, workRoot, args, cancellationToken, progress, 22, 88);

            progress?.Report(new(ConversionStage.BuildingPackage, 91, "Signing the rebuilt APK with the modern JDK signing tools"));
            await SignGeneratedApkAsync(stagedOutput, request.TitleId, cancellationToken);

            progress?.Report(new(ConversionStage.ValidatingOutput, 97, "Verifying final APK signature, game ZPAK, and TOC"));
            ValidateFinalOutput(stagedOutput, request.TitleId);
            var finalFolder = Path.Combine(Path.GetFullPath(request.OutputDirectory), "PSXperia-" + request.TitleId);
            if (Directory.Exists(finalFolder) || File.Exists(finalFolder))
                throw new IOException($"Output already exists: {finalFolder}. Choose another folder or remove the previous output first.");
            Directory.Move(stagedOutput, finalFolder);
            progress?.Report(new(ConversionStage.Completed, 100, $"Installable APK and matching game data created in {finalFolder}"));
        }
        finally
        {
            try { if (Directory.Exists(workRoot)) Directory.Delete(workRoot, recursive: true); } catch { /* keep primary conversion result/error */ }
        }
    }

    private static async Task RunJavaAsync(string java, string jar, string workingDirectory, IReadOnlyList<string> commandArgs,
        CancellationToken cancellationToken, IProgress<ConversionProgress>? progress, int startPercent, int endPercent)
    {
        var start = new ProcessStartInfo(java)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-cp"); start.ArgumentList.Add(jar);
        start.ArgumentList.Add(MainClass);
        foreach (var arg in commandArgs) start.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Could not start the Java converter process.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("Java was not found. Install a full JDK 21 and set JAVA_HOME to its installation folder so java.exe, keytool.exe, and jarsigner.exe are available, then restart PSXperia Remastered.", ex);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        var output = (await stdout) + Environment.NewLine + (await stderr);
        if (process.ExitCode != 0 || output.Contains("[E]", StringComparison.Ordinal) || output.Contains("Exception", StringComparison.Ordinal))
            throw new InvalidOperationException($"Original PSXperia converter reported a failure (exit code {process.ExitCode}).\n\n{Tail(output, 7000)}");
        progress?.Report(new(ConversionStage.BuildingPackage, endPercent, "Original converter stage completed"));
    }


    private static async Task SignGeneratedApkAsync(string outputDirectory, string titleId, CancellationToken cancellationToken)
    {
        var apkPath = Path.Combine(outputDirectory, $"com.sony.playstation.{titleId}.apk");
        var unsignedPath = apkPath + ".unsigned";
        if (!File.Exists(unsignedPath))
            throw new FileNotFoundException("The original packager did not produce its expected unsigned APK.", unsignedPath);

        var keytool = ResolveJdkTool("keytool");
        var jarsigner = ResolveJdkTool("jarsigner");
        var signingDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PSXperiaRemastered", "signing");
        Directory.CreateDirectory(signingDirectory);
        var keyStore = Path.Combine(signingDirectory, "psxperia-release.p12");
        var passwordFile = Path.Combine(signingDirectory, "keystore-password.txt");
        var password = await GetOrCreateSigningPasswordAsync(passwordFile, File.Exists(keyStore), cancellationToken);

        if (!File.Exists(keyStore))
        {
            var createArgs = new[]
            {
                "-genkeypair", "-v", "-keystore", keyStore, "-storetype", "PKCS12",
                "-storepass", password, "-keypass", password, "-alias", "psxperia-remastered",
                "-keyalg", "RSA", "-keysize", "2048", "-sigalg", "SHA256withRSA",
                "-validity", "10000", "-dname", "CN=PSXperia Remastered, OU=Local Signing, O=PSXperia Remastered, C=US", "-noprompt"
            };
            await RunExternalToolAsync(keytool, createArgs, signingDirectory, cancellationToken);
            if (!File.Exists(keyStore)) throw new InvalidOperationException("JDK keytool completed without creating the signing keystore.");
        }

        var signArgs = new[]
        {
            "-keystore", keyStore, "-storetype", "PKCS12", "-storepass", password,
            "-keypass", password, "-sigalg", "SHA256withRSA", "-digestalg", "SHA-256",
            "-signedjar", apkPath, unsignedPath, "psxperia-remastered"
        };
        await RunExternalToolAsync(jarsigner, signArgs, outputDirectory, cancellationToken);
        if (!File.Exists(apkPath) || new FileInfo(apkPath).Length == 0)
            throw new InvalidDataException("JDK jarsigner did not produce the signed APK.");

        await RunExternalToolAsync(jarsigner, new[] { "-verify", "-verbose", apkPath }, outputDirectory, cancellationToken);
        File.Delete(unsignedPath);
    }

    private static async Task<string> GetOrCreateSigningPasswordAsync(string passwordFile, bool keyStoreAlreadyExists, CancellationToken cancellationToken)
    {
        if (File.Exists(passwordFile))
        {
            var existing = (await File.ReadAllTextAsync(passwordFile, cancellationToken)).Trim();
            if (existing.Length >= 32) return existing;
            throw new InvalidOperationException("The local APK-signing password file is invalid. Restore the matching signing password backup before converting more games.");
        }
        if (keyStoreAlreadyExists)
            throw new InvalidOperationException("The local APK-signing keystore exists but its password file is missing. The key was not replaced; restore the password file from your PSXperia Remastered signing backup.");

        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(36);
        var password = Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "");
        await File.WriteAllTextAsync(passwordFile, password, cancellationToken);
        return password;
    }

    private static string ResolveJdkTool(string toolName)
    {
        var executable = OperatingSystem.IsWindows() ? toolName + ".exe" : toolName;
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            var candidate = Path.Combine(javaHome, "bin", executable);
            if (File.Exists(candidate)) return candidate;
        }
        // PATH lookup is intentionally delegated to Windows/process creation when no JAVA_HOME is set.
        return executable;
    }

    private static async Task RunExternalToolAsync(string executable, IReadOnlyList<string> args, string workingDirectory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"{Path.GetFileName(executable)} was not found. Install a full JDK 21 and set JAVA_HOME so its bin folder contains java.exe, keytool.exe, and jarsigner.exe.", ex);
        }
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await stdout) + Environment.NewLine + (await stderr);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(executable)} failed (exit code {process.ExitCode}).\n\n{Tail(output, 5000)}");
    }

    private static void ValidateFinalOutput(string outputDirectory, string titleId)
    {
        var apkPath = Path.Combine(outputDirectory, $"com.sony.playstation.{titleId}.apk");
        var content = Path.Combine(outputDirectory, "data", $"com.sony.playstation.{titleId}", "files", "content");
        var zpakPath = Path.Combine(content, titleId + ".zpak");
        var tocPath = Path.Combine(content, "image_ps_toc.bin");
        if (!File.Exists(apkPath)) throw new InvalidDataException("Original converter did not create the expected game APK.");
        if (!File.Exists(zpakPath)) throw new InvalidDataException("Original converter did not create the game ZPAK in Android/data.");
        if (!File.Exists(tocPath) || new FileInfo(tocPath).Length == 0) throw new InvalidDataException("Original converter did not create a valid TOC file.");
        using var apk = ZipFile.OpenRead(apkPath);
        if (apk.GetEntry("AndroidManifest.xml") is null || apk.GetEntry("classes.dex") is null)
            throw new InvalidDataException("Generated APK is missing its Android manifest or DEX payload.");
        using var zpak = ZipFile.OpenRead(zpakPath);
        if (zpak.GetEntry("data/image.ps") is null)
            throw new InvalidDataException("Generated game ZPAK is missing data/image.ps.");
        if (zpak.GetEntry("metadata.xml") is null)
            throw new InvalidDataException("Generated game ZPAK is missing Sony metadata.xml.");
    }

    private static string ResolveDataImagePath(string imagePath)
    {
        if (Path.GetExtension(imagePath).Equals(".cue", StringComparison.OrdinalIgnoreCase))
        {
            var inspection = DiscImageInspector.Inspect(imagePath);
            var dataTrack = inspection.Tracks.FirstOrDefault(t => t.Mode != DiscTrackMode.Audio);
            if (dataTrack is null) throw new InvalidDataException("The CUE sheet does not contain a readable data track. Audio-only discs cannot be converted.");
            return Path.GetFullPath(dataTrack.FilePath);
        }
        return Path.GetFullPath(imagePath);
    }

    private static bool IsValidTitleId(string value) => !string.IsNullOrWhiteSpace(value) && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    private static string ResolveJavaExecutable() => OperatingSystem.IsWindows() ? "java.exe" : "java";
    private static string Tail(string value, int max) => value.Length <= max ? value : value[^max..];
}
