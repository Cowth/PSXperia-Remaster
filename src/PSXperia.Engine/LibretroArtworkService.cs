using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PSXperia.Engine;

public sealed record ArtworkCandidate(string FileName, string DownloadUrl, string RegionLabel, string VariantLabel);

/// <summary>Looks up box art in Libretro's Sony - PlayStation Named_Boxarts directory.</summary>
public sealed class LibretroArtworkService
{
    private const string ApiRoot = "https://api.github.com/repos/libretro-thumbnails/Sony_-_PlayStation";
    private static readonly HttpClient Client = CreateClient();
    private static readonly string[] RegionTokens = { "USA", "Europe", "Japan", "World", "Brazil", "Germany", "France", "Spain", "Italy", "Australia" };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PSXperia-Remastered/0.5 (personal-library-artwork-lookup)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public async Task<IReadOnlyList<ArtworkCandidate>> FindCandidatesAsync(string title, string? region, CancellationToken cancellationToken = default)
    {
        var cleanTitle = CleanTitle(title);
        if (cleanTitle.Length < 1) return Array.Empty<ArtworkCandidate>();

        using var rootResponse = await Client.GetAsync($"{ApiRoot}/git/trees/master", cancellationToken);
        if (rootResponse.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("The Libretro artwork repository branch could not be found.");
        rootResponse.EnsureSuccessStatusCode();
        using var rootJson = JsonDocument.Parse(await rootResponse.Content.ReadAsStringAsync(cancellationToken));
        var rootTree = rootJson.RootElement.GetProperty("tree");
        var folder = rootTree.EnumerateArray().FirstOrDefault(x => x.TryGetProperty("path", out var p) && p.GetString() == "Named_Boxarts");
        if (folder.ValueKind == JsonValueKind.Undefined || !folder.TryGetProperty("sha", out var sha))
            throw new InvalidDataException("Named_Boxarts directory was not found in the Libretro repository.");

        using var artResponse = await Client.GetAsync($"{ApiRoot}/git/trees/{sha.GetString()}?recursive=1", cancellationToken);
        artResponse.EnsureSuccessStatusCode();
        using var artJson = JsonDocument.Parse(await artResponse.Content.ReadAsStringAsync(cancellationToken));
        var tree = artJson.RootElement.GetProperty("tree");
        var titleWords = Tokenize(cleanTitle);
        var matches = new List<(ArtworkCandidate Candidate, int Score)>();

        foreach (var item in tree.EnumerateArray())
        {
            if (!item.TryGetProperty("path", out var pathNode)) continue;
            var path = pathNode.GetString() ?? string.Empty;
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
            var file = Path.GetFileNameWithoutExtension(path);
            var normalized = CleanTitle(file);
            var candidateWords = Tokenize(normalized);
            var exact = normalized.Equals(cleanTitle, StringComparison.OrdinalIgnoreCase);
            var overlap = titleWords.Intersect(candidateWords, StringComparer.OrdinalIgnoreCase).Count();
            // One-word titles (notably the PS1 game "D") are valid exact searches.
            // For longer titles require a meaningful token overlap to avoid unrelated hits.
            if (!exact && (overlap < (titleWords.Length == 1 ? 1 : 2) ||
                overlap < Math.Ceiling(Math.Min(titleWords.Length, candidateWords.Length) * 0.65))) continue;
            if (exact) overlap = Math.Max(1, titleWords.Length);
            var score = overlap * 10 - Math.Abs(titleWords.Length - candidateWords.Length) * 2;
            if (exact) score += 100;
            else if (normalized.StartsWith(cleanTitle, StringComparison.OrdinalIgnoreCase) || cleanTitle.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)) score += 25;
            if (!string.IsNullOrWhiteSpace(region) && file.Contains(region, StringComparison.OrdinalIgnoreCase)) score += 30;
            var regionLabel = RegionTokens.FirstOrDefault(r => file.Contains(r, StringComparison.OrdinalIgnoreCase)) ?? "Region unspecified";
            var variant = Regex.Replace(file, @"\s*\([^)]*\)", " ").Trim();
            matches.Add((new ArtworkCandidate(file + ".png",
                $"https://raw.githubusercontent.com/libretro-thumbnails/Sony_-_PlayStation/master/Named_Boxarts/{Uri.EscapeDataString(file)}.png",
                regionLabel, variant.Equals(file, StringComparison.OrdinalIgnoreCase) ? "Artwork variant" : variant), score));
        }

        return matches.OrderByDescending(x => x.Score).ThenBy(x => x.Candidate.FileName, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Score >= 10).Take(16).Select(x => x.Candidate).ToArray();
    }

    public async Task<byte[]> DownloadAsync(ArtworkCandidate candidate, CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(candidate.DownloadUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length < 8 || bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47)
            throw new InvalidDataException("The artwork provider did not return a valid PNG image.");
        return bytes;
    }

    private static string CleanTitle(string value)
    {
        value = Regex.Replace(value, @"\([^)]*(?:USA|Europe|Japan|Disc\s*\d+|Rev\s*\d+)[^)]*\)", " ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\([^)]*\)", " ");
        value = Regex.Replace(value, @"[^A-Za-z0-9]+", " ");
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static string[] Tokenize(string value) => value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
