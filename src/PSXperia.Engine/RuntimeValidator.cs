using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace PSXperia.Engine;

/// <summary>Validates user-selected Sony runtime files without modifying or copying them.</summary>
public sealed class RuntimeValidator : IRuntimeValidator
{
    private static readonly HashSet<string> SupportedOriginalApkCrcs = new(StringComparer.OrdinalIgnoreCase) { "76C201D8", "7C33475B", "BB542581", "E7BCB6D5" };
    private static readonly string[] RequiredApkEntries =
    ["AndroidManifest.xml", "classes.dex", "resources.arsc", "lib/armeabi/libjava-activity.so", "lib/armeabi/libzplatform.so"];

    public async Task<RuntimeValidationResult> ValidateAsync(RuntimeSelection selection, CancellationToken cancellationToken = default)
    {
        var checks = new List<string>(); var warnings = new List<string>(); var errors = new List<string>();
        string? titleId = null, runtimeVersion = null, sha = null, crc = null;
        if (!File.Exists(selection.ApkPath)) errors.Add("The selected APK does not exist.");
        if (!File.Exists(selection.ZpakPath)) errors.Add("The selected ZPAK does not exist.");
        if (errors.Count != 0) return new(false, null, null, null, null, checks, warnings, errors);

        try
        {
            await using var apkStream = new FileStream(selection.ApkPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
            using var apk = new ZipArchive(apkStream, ZipArchiveMode.Read, leaveOpen: true);
            var names = apk.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var required in RequiredApkEntries)
                if (!names.Contains(required)) errors.Add($"APK is missing required entry: {required}");
            if (errors.Count == 0) checks.Add("APK archive and required emulator files are present.");

            var embeddedEntry = apk.Entries.FirstOrDefault(e => e.FullName.EndsWith(".zpak", StringComparison.OrdinalIgnoreCase));
            // The Sony APK stores its default ZPAK as a nested ZIP file under /assets.
            if (embeddedEntry is not null)
            {
                await using var nestedStream = embeddedEntry.Open();
                using var nested = new ZipArchive(nestedStream, ZipArchiveMode.Read, leaveOpen: false);
                var metaEntry = nested.GetEntry("metadata.xml");
                if (metaEntry is not null)
                {
                    await using var s = metaEntry.Open(); var doc = await XDocument.LoadAsync(s, LoadOptions.None, cancellationToken);
                    titleId = doc.Root?.Element("title_id")?.Value;
                    runtimeVersion = doc.Root?.Attribute("version")?.Value;
                    if (!string.IsNullOrWhiteSpace(titleId)) checks.Add($"Embedded runtime metadata found ({titleId}).");
                }
                else warnings.Add("Embedded APK ZPAK has no root metadata.xml.");
            }
            else warnings.Add("Could not locate the embedded default ZPAK in the APK; external ZPAK metadata will be used.");

            apkStream.Position = 0;
            using var sha256 = SHA256.Create(); sha = Convert.ToHexString(await sha256.ComputeHashAsync(apkStream, cancellationToken));
            apkStream.Position = 0;
            var crc32 = new Crc32Stream(); await crc32.ComputeAsync(apkStream, cancellationToken); crc = crc32.Value.ToString("X8");
            checks.Add($"APK SHA-256 and CRC32 calculated ({crc}).");
            if (!SupportedOriginalApkCrcs.Contains(crc))
                errors.Add($"APK CRC32 {crc} is not in the original PSXperia patch table. Only exact supported Crash Bandicoot APK builds can be converted.");
            else checks.Add("Exact APK build is present in the original PSXperia patch table.");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.Xml.XmlException)
        { errors.Add($"APK validation failed: {ex.Message}"); }

        try
        {
            await using var zstream = new FileStream(selection.ZpakPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
            using var zpak = new ZipArchive(zstream, ZipArchiveMode.Read, leaveOpen: true);
            var entries = zpak.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!entries.Contains("metadata.xml")) errors.Add("ZPAK is missing metadata.xml at its root.");
            if (!entries.Contains("data/image.ps")) errors.Add("ZPAK is missing data/image.ps.");
            if (!entries.Contains("data/ps1_rom.bin")) warnings.Add("ZPAK does not contain data/ps1_rom.bin.");
            if (entries.Contains("metadata.xml"))
            {
                var meta = zpak.GetEntry("metadata.xml")!;
                await using var ms = meta.Open(); var doc = await XDocument.LoadAsync(ms, LoadOptions.None, cancellationToken);
                var externalId = doc.Root?.Element("title_id")?.Value;
                var version = doc.Root?.Element("dependencies")?.Element("title_version")?.Value;
                if (!string.IsNullOrWhiteSpace(externalId))
                {
                    if (titleId is not null && !string.Equals(titleId, externalId, StringComparison.OrdinalIgnoreCase))
                        errors.Add($"APK/ZPAK title ID mismatch: {titleId} vs {externalId}.");
                    titleId ??= externalId;
                }
                runtimeVersion ??= version;
                checks.Add($"External ZPAK metadata parsed (version {version ?? "unspecified"}).");
            }
            if (errors.Count == 0) checks.Add("ZPAK archive structure validated.");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.Xml.XmlException)
        { errors.Add($"ZPAK validation failed: {ex.Message}"); }

        return new(errors.Count == 0, titleId, runtimeVersion, sha, crc, checks, warnings, errors);
    }

    private sealed class Crc32Stream
    {
        private uint _crc = 0xFFFFFFFF;
        public uint Value => ~_crc;
        private static readonly uint[] Table = BuildTable();
        public async Task ComputeAsync(Stream stream, CancellationToken ct)
        {
            var buffer = new byte[128 * 1024]; int n;
            while ((n = await stream.ReadAsync(buffer, ct)) > 0)
                for (var i = 0; i < n; i++) _crc = Table[(_crc ^ buffer[i]) & 0xFF] ^ (_crc >> 8);
        }
        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++) { var c = i; for (var j = 0; j < 8; j++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; table[i] = c; }
            return table;
        }
    }
}
