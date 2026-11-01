using PSXperia.Engine;

namespace PSXperia.Engine.Tests;

public sealed class EngineValidationTests
{
    [Theory]
    [InlineData("unsupported.chd")]
    [InlineData("unsupported.txt")]
    [Trait("Category", "Unit")]
    public async Task CliEngine_RejectsUnsupportedImageExtensions(string name)
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteFile(name, [1, 2, 3]);
        var engine = new OriginalPsxperiaCliEngine(new RuntimeValidator());

        await Assert.ThrowsAsync<InvalidDataException>(() => engine.ValidateInputAsync(path));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CliEngine_RejectsMissingAndEmptyImages()
    {
        using var workspace = new TestWorkspace();
        var missing = workspace.PathFor("missing.iso");
        var empty = workspace.WriteFile("empty.iso", []);
        var engine = new OriginalPsxperiaCliEngine(new RuntimeValidator());

        await Assert.ThrowsAsync<FileNotFoundException>(() => engine.ValidateInputAsync(missing));
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.ValidateInputAsync(empty));
    }
}
