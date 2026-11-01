namespace PSXperia.Engine.Tests;

internal sealed class TestWorkspace : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "PSXperia.Engine.Tests", Guid.NewGuid().ToString("N"));

    public TestWorkspace() => Directory.CreateDirectory(Root);

    public string PathFor(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(Root, relativePath));
        var rootPrefix = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Test fixture path escaped its isolated workspace.");
        return path;
    }

    public string WriteFile(string relativePath, byte[] bytes)
    {
        var path = PathFor(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public string WriteText(string relativePath, string text) => WriteFile(relativePath, System.Text.Encoding.UTF8.GetBytes(text));

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
