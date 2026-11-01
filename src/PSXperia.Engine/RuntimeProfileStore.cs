using System.Text.Json;

namespace PSXperia.Engine;

/// <summary>Persists paths only; proprietary APK/ZPAK bytes are never copied into the application.</summary>
public sealed class RuntimeProfileStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PSXperiaRemastered", "runtime-profile.json");
    public async Task<RuntimeSelection?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return null;
        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<RuntimeSelection>(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }
    public async Task SaveAsync(RuntimeSelection selection, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            await JsonSerializer.SerializeAsync(stream, selection, cancellationToken: cancellationToken);
        File.Move(temp, _path, overwrite: true);
    }
}
