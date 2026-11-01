using PSXperia.Engine;

namespace PSXperia.Engine.Tests;

public sealed class ConversionFailureCleanupTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task CliEngine_CleansTemporaryWorkspaceWhenSyntheticRuntimeExtractionFails()
    {
        Assert.True(OperatingSystem.IsWindows(), "This integration test exercises the Windows java.exe invocation used by the application.");

        using var workspace = new TestWorkspace();
        var jar = Path.Combine(AppContext.BaseDirectory, "tools", "PSXperiaTool.jar");
        Assert.True(File.Exists(jar), "The test project must copy the licensed converter JAR beside the test output.");

        var priorTemp = Environment.GetEnvironmentVariable("TEMP");
        var priorTmp = Environment.GetEnvironmentVariable("TMP");
        var priorJavaToolOptions = Environment.GetEnvironmentVariable("JAVA_TOOL_OPTIONS");
        var isolatedTemp = workspace.PathFor("isolated-temp");
        Directory.CreateDirectory(isolatedTemp);
        Environment.SetEnvironmentVariable("TEMP", isolatedTemp);
        Environment.SetEnvironmentVariable("TMP", isolatedTemp);
        Environment.SetEnvironmentVariable("JAVA_TOOL_OPTIONS", CombineJavaOptions(priorJavaToolOptions, "-XX:-UsePerfData"));
        try
        {
            var sourceImage = workspace.WriteFile("synthetic.iso", [0x00, 0x01, 0x02, 0x03]);
            var syntheticApk = workspace.WriteText("synthetic-runtime.apk", "synthetic test-only APK payload");
            var syntheticZpak = workspace.WriteText("synthetic-runtime.zpak", "synthetic test-only ZPAK payload");
            var output = workspace.PathFor("output");
            Directory.CreateDirectory(output);
            var engine = new OriginalPsxperiaCliEngine(new AllowlistedSyntheticValidator());

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ConvertAsync(
                new ConversionRequest(sourceImage, output, "SLUS12345_1", "Synthetic Test", Runtime: new RuntimeSelection(syntheticApk, syntheticZpak))));
            Assert.Contains("Original PSXperia converter reported a failure (exit code ", exception.Message, StringComparison.Ordinal);

            var workRoot = Path.Combine(isolatedTemp, "PSXperiaRemastered");
            Assert.True(!Directory.Exists(workRoot) || !Directory.EnumerateFileSystemEntries(workRoot).Any(),
                "The active engine should remove its temporary workspace after the synthetic extraction failure.");
            Assert.Empty(Directory.EnumerateFileSystemEntries(output));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEMP", priorTemp);
            Environment.SetEnvironmentVariable("TMP", priorTmp);
            Environment.SetEnvironmentVariable("JAVA_TOOL_OPTIONS", priorJavaToolOptions);
        }
    }

    private static string CombineJavaOptions(string? existing, string option) =>
        string.IsNullOrWhiteSpace(existing) ? option : $"{existing} {option}";

    private sealed class AllowlistedSyntheticValidator : IRuntimeValidator
    {
        public Task<RuntimeValidationResult> ValidateAsync(RuntimeSelection selection, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RuntimeValidationResult(true, "CB_TEST", "synthetic", "not-a-real-runtime", "76C201D8", [], [], []));
    }
}
