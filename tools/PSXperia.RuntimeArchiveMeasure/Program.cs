namespace PSXperia.RuntimeArchiveMeasure;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            PrintHelp();
            return 0;
        }

        var options = Parse(args);
        if (options.Demo)
        {
            var sample = await SyntheticDemo.RunAsync(options.Budget);
            var bounded = ReportOutput.ToBoundedJson(sample);
            Console.WriteLine(bounded.Json);
            return MeasurementExitCode.For(bounded.Report);
        }

        var apkPath = options.ApkPath ?? PromptPath("Path to the local runtime APK");
        var zpakPath = options.ZpakPath ?? PromptPath("Path to the matching local runtime ZPAK");
        EnsureLocalInput(apkPath);
        EnsureLocalInput(zpakPath);
        var budget = options.Budget ?? ScanBudget.Default;
        var report = await new ArchiveMeasurementService().MeasureAsync(apkPath, zpakPath, budget);

        var outputPath = options.OutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            var outputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PSXperiaRemastered", "ArchiveMeasurements");
            outputPath = Path.Combine(outputDirectory, $"measurement-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
        }

        report = await ReportOutput.SaveJsonAsync(report, outputPath, options.AllowSharedOutput);
        Console.WriteLine($"Report written locally to: {Path.GetFullPath(outputPath)}");
        Console.WriteLine($"APK status: {report.Apk.Status}; external ZPAK status: {report.ExternalZpak.Status}");
        if (!report.IsComplete)
            Console.WriteLine("One or more measurements are incomplete. See the report reasons; incomplete values are not inferred.");
        return MeasurementExitCode.For(report);
    }

    private static Options Parse(string[] args)
    {
        string? apk = null, zpak = null, output = null;
        var demo = false;
        var allowShared = false;
        var budget = ScanBudget.Default;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => ++i < args.Length ? args[i] : throw new ArgumentException($"Missing value after {args[i - 1]}.");
            switch (args[i])
            {
                case "--apk": apk = Next(); break;
                case "--zpak": zpak = Next(); break;
                case "--output": output = Next(); break;
                case "--demo": demo = true; break;
                case "--allow-shared-output": allowShared = true; break;
                default: throw new ArgumentException($"Unknown option: {args[i]}");
            }
        }
        return new(apk, zpak, output, demo, allowShared, budget);
    }

    private static string PromptPath(string prompt)
    {
        Console.Write(prompt + ": ");
        return Console.ReadLine()?.Trim().Trim('"') is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException("A local archive path is required.");
    }

    private static void EnsureLocalInput(string path)
    {
        var full = Path.GetFullPath(path);
        if (ReportOutput.IsRemotePath(full))
            throw new InvalidOperationException("Runtime archive measurement accepts local files only.");
        if (ReportOutput.HasExistingReparseComponent(full))
            throw new InvalidOperationException("Runtime archive measurement refuses paths containing symbolic links or reparse points.");
    }

    private static void PrintHelp() => Console.WriteLine("""
        PSXperia Runtime Archive Measurement Utility (read-only)

        Interactive:
          dotnet run --project tools/PSXperia.RuntimeArchiveMeasure

        Explicit local files:
          dotnet run --project tools/PSXperia.RuntimeArchiveMeasure -- --apk <path> --zpak <path> [--output <local-report.json>]

        Synthetic sample report:
          dotnet run --project tools/PSXperia.RuntimeArchiveMeasure -- --demo

        Fixed scanner/report safety budgets are included in each report. A budget
        hit produces an incomplete report; it does not mean the
        archive is invalid or unsupported. Reports default to LocalApplicationData.
        Use --allow-shared-output only when you intentionally choose a network report path.
        """);

    private sealed record Options(string? ApkPath, string? ZpakPath, string? OutputPath,
        bool Demo, bool AllowSharedOutput, ScanBudget? Budget);
}
