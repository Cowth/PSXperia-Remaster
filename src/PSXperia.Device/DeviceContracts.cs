namespace PSXperia.Device;
public sealed record XperiaDevice(string Serial, string Model, string AndroidVersion);
public interface IXperiaDeviceService
{
    Task<IReadOnlyList<XperiaDevice>> FindDevicesAsync(CancellationToken cancellationToken = default);
    Task InstallPackageAsync(string deviceSerial, string apkPath, CancellationToken cancellationToken = default);
    Task TransferGameDataAsync(string deviceSerial, string sourceDirectory, string packageName, CancellationToken cancellationToken = default);
}
