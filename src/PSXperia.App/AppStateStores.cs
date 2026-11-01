using System.IO;
using System.Text.Json;

namespace PSXperia.App;

public sealed record ConvertedGameRecord(
    Guid Id, string Title, string TitleId, string ApkPath, string DataPath,
    string OutputFolder, string? CoverPath, DateTime ConvertedAt);

public sealed record CompatibilityRecord(
    string TitleId, string GameTitle, string Status, string Symptoms, string Notes, DateTime UpdatedAt);

public sealed class UserSettings
{
    public bool AnimatedBackground { get; set; } = true;
    public bool UiSounds { get; set; } = false;
    public bool ReduceMotion { get; set; } = false;
    public bool AutomaticArtwork { get; set; } = true;
    public bool BackgroundMusic { get; set; } = true;
    public double BackgroundMusicVolume { get; set; } = 0.10;
}

internal sealed class AppStateStore
{
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PSXperiaRemastered");
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    private string PathFor(string name) => Path.Combine(_root, name);

    public async Task<List<ConvertedGameRecord>> LoadConvertedAsync()
        => await LoadAsync<List<ConvertedGameRecord>>("converted-games.json") ?? new();

    public Task SaveConvertedAsync(IEnumerable<ConvertedGameRecord> items)
        => SaveAsync("converted-games.json", items.ToList());

    public async Task<List<CompatibilityRecord>> LoadCompatibilityAsync()
        => await LoadAsync<List<CompatibilityRecord>>("compatibility.json") ?? new();

    public Task SaveCompatibilityAsync(IEnumerable<CompatibilityRecord> items)
        => SaveAsync("compatibility.json", items.ToList());

    public async Task<UserSettings> LoadSettingsAsync()
        => await LoadAsync<UserSettings>("settings.json") ?? new UserSettings();

    public Task SaveSettingsAsync(UserSettings settings)
        => SaveAsync("settings.json", settings);

    private async Task<T?> LoadAsync<T>(string file)
    {
        var path = PathFor(file);
        if (!File.Exists(path)) return default;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
        return await JsonSerializer.DeserializeAsync<T>(stream, _options);
    }

    private async Task SaveAsync<T>(string file, T value)
    {
        Directory.CreateDirectory(_root);
        var path = PathFor(file);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(stream, value, _options);
                await stream.FlushAsync();
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
