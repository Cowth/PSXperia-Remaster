using System.Text;
using System.Text.RegularExpressions;

namespace PSXperia.Engine;

/// <summary>Best-effort PS1 product-code scanner. It only reads the beginning of data tracks.</summary>
public static class DiscSerialScanner
{
    private static readonly Regex SerialPattern = new(
        @"(?<![A-Z0-9])(?<serial>(?:SCUS|SLUS|SCES|SLES|SCPS|SLPS|SIPS|PAPX|PBPX|PCPX)[_-]\s*\d{3,5}(?:\.\d{2})?)(?![A-Z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string? FindSerial(DiscImageInspection inspection, int maxBytesPerFile = 32 * 1024 * 1024)
    {
        // Only inspect files that the CUE identifies as data tracks. Audio tracks can
        // contain arbitrary bytes that accidentally resemble a product code.
        var dataFiles = inspection.Tracks.Count > 0
            ? inspection.Tracks.Where(t => t.Mode != DiscTrackMode.Audio).Select(t => t.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : inspection.DataFiles;
        foreach (var file in dataFiles)
        {
            if (!File.Exists(file)) continue;
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                var limit = Math.Min(stream.Length, Math.Max(maxBytesPerFile, 64 * 1024 * 1024));
                var buffer = new byte[1024 * 1024 + 64];
                var carry = 0;
                long consumed = 0;
                while (consumed < limit)
                {
                    var wanted = (int)Math.Min(buffer.Length - carry, limit - consumed);
                    var read = stream.Read(buffer, carry, wanted);
                    if (read <= 0) break;
                    var count = carry + read;
                    var ascii = Encoding.ASCII.GetString(buffer, 0, count);
                    var match = SerialPattern.Match(ascii);
                    if (match.Success)
                        return Regex.Replace(match.Groups["serial"].Value, @"\s+", string.Empty).Replace('_', '-').ToUpperInvariant();

                    consumed += read;
                    carry = Math.Min(64, count);
                    Buffer.BlockCopy(buffer, count - carry, buffer, 0, carry);
                }
            }
            catch (IOException) { /* Keep searching other data tracks. */ }
            catch (UnauthorizedAccessException) { /* Import remains possible; conversion will validate access. */ }
        }
        return null;
    }
}
