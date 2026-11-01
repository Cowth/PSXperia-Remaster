using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace PSXperia.Device;

/// <summary>ADB-backed device access. Uses an installed Android platform-tools adb.exe; does not bundle Google binaries.</summary>
public sealed class AdbXperiaDeviceService : IXperiaDeviceService
{
    private readonly string _adbPath;

    public AdbXperiaDeviceService(string? adbPath = null)
    {
        _adbPath = ResolveAdbPath(adbPath) ?? throw new FileNotFoundException(
            "Android Debug Bridge (adb.exe) was not found. Install Android SDK Platform-Tools or place adb.exe on PATH.");
    }

    public string AdbPath => _adbPath;

    public static string? ResolveAdbPath(string? explicitPath = null)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(explicitPath)) candidates.Add(explicitPath);
        var env = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT") ?? Environment.GetEnvironmentVariable("ANDROID_HOME");
        if (!string.IsNullOrWhiteSpace(env)) candidates.Add(Path.Combine(env, "platform-tools", "adb.exe"));
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local)) candidates.Add(Path.Combine(local, "Android", "Sdk", "platform-tools", "adb.exe"));
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles)) candidates.Add(Path.Combine(programFiles, "Android", "platform-tools", "adb.exe"));
        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(folder.Trim('"'), "adb.exe");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }

    public async Task<IReadOnlyList<XperiaDevice>> FindDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync("devices -l", cancellationToken);
        if (result.ExitCode != 0) throw new InvalidOperationException(FormatFailure("ADB could not list devices", result));
        var devices = new List<XperiaDevice>();
        foreach (var line in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1))
        {
            var match = Regex.Match(line, @"^(\S+)\s+device(?:\s+(.*))?$");
            if (!match.Success) continue; // unauthorized/offline devices are intentionally not treated as ready.
            var serial = match.Groups[1].Value;
            var model = await GetPropAsync(serial, "ro.product.model", cancellationToken);
            var android = await GetPropAsync(serial, "ro.build.version.release", cancellationToken);
            var manufacturer = await GetPropAsync(serial, "ro.product.manufacturer", cancellationToken);
            if (string.IsNullOrWhiteSpace(model)) model = match.Groups[2].Value;
            if (!string.IsNullOrWhiteSpace(manufacturer) && !model.Contains(manufacturer, StringComparison.OrdinalIgnoreCase)) model = manufacturer + " " + model;
            devices.Add(new XperiaDevice(serial, string.IsNullOrWhiteSpace(model) ? "Android device" : model.Trim(), string.IsNullOrWhiteSpace(android) ? "Unknown" : android.Trim()));
        }
        return devices;
    }

    public async Task InstallPackageAsync(string deviceSerial, string apkPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(apkPath)) throw new FileNotFoundException("The selected APK does not exist.", apkPath);
        var result = await RunAsync($"-s {Quote(deviceSerial)} install -r {Quote(apkPath)}", cancellationToken, TimeSpan.FromMinutes(3));
        if (result.ExitCode != 0 || !result.StdOut.Contains("Success", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(FormatFailure("APK installation failed", result));
    }

    public async Task TransferGameDataAsync(string deviceSerial, string sourceDirectory, string packageName, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException($"Game data folder was not found: {sourceDirectory}");
        if (!Regex.IsMatch(packageName, @"^[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+$")) throw new ArgumentException("Invalid Android package name.", nameof(packageName));
        var destination = $"/sdcard/Android/data/{packageName}/";
        var result = await RunAsync($"-s {Quote(deviceSerial)} push {Quote(Path.GetFullPath(sourceDirectory) + Path.DirectorySeparatorChar + ".")} {Quote(destination)}", cancellationToken, TimeSpan.FromMinutes(10));
        if (result.ExitCode != 0 || !result.StdOut.Contains("pushed", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(FormatFailure("Game-data transfer failed", result));
    }

    public async Task<string> GetStorageSummaryAsync(string deviceSerial, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync($"-s {Quote(deviceSerial)} shell df /sdcard", cancellationToken);
        if (result.ExitCode != 0) return "Storage information unavailable";
        return result.StdOut.Trim();
    }

    public async Task<bool> VerifyPackageInstalledAsync(string deviceSerial, string packageName, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync($"-s {Quote(deviceSerial)} shell pm path {Quote(packageName)}", cancellationToken);
        return result.ExitCode == 0 && result.StdOut.Contains("package:", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> GetPropAsync(string serial, string property, CancellationToken token)
    {
        var result = await RunAsync($"-s {Quote(serial)} shell getprop {property}", token);
        return result.ExitCode == 0 ? result.StdOut.Trim() : "";
    }

    private async Task<ProcessResult> RunAsync(string arguments, CancellationToken token, TimeSpan? timeout = null)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo
        {
            FileName = _adbPath, Arguments = arguments, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        }};
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Could not start adb.exe.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(35));
            try { await process.WaitForExitAsync(timeoutCts.Token); }
            catch (OperationCanceledException) { try { process.Kill(true); } catch { } throw; }
            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }
        catch (System.ComponentModel.Win32Exception ex)
        { throw new InvalidOperationException($"Could not launch ADB at '{_adbPath}'. {ex.Message}", ex); }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    private static string FormatFailure(string title, ProcessResult result) => $"{title}. Exit code: {result.ExitCode}\n{result.StdErr}\n{result.StdOut}".Trim();
    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
