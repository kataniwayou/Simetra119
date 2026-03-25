using System.Diagnostics.CodeAnalysis;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Provides O(1) device lookup by IP+Port (primary) or device name (secondary).
/// </summary>
public interface IDeviceRegistry
{
    /// <summary>
    /// Attempts to find a registered device by its configured address and port.
    /// Primary lookup used by poll jobs that receive address+port from Quartz JobDataMap.
    /// The address matches the raw config value (DNS name or IP), not the resolved IP.
    /// </summary>
    /// <param name="configAddress">The device address as configured (DNS name or IP).</param>
    /// <param name="port">The device SNMP port.</param>
    /// <param name="device">The device info if found; null otherwise.</param>
    /// <returns>True if the device was found; false otherwise.</returns>
    bool TryGetByIpPort(string configAddress, int port, [NotNullWhen(true)] out DeviceInfo? device);

    /// <summary>
    /// Attempts to find a registered device by its configured name.
    /// Secondary lookup used by trap listener that knows the device name from community string.
    /// Lookup is case-insensitive (device names are user-configured).
    /// </summary>
    /// <param name="deviceName">The device name to look up.</param>
    /// <param name="device">The device info if found; null otherwise.</param>
    /// <returns>True if the device was found; false otherwise.</returns>
    bool TryGetDeviceByName(string deviceName, [NotNullWhen(true)] out DeviceInfo? device);

    /// <summary>
    /// All registered devices, in configuration order.
    /// Used by the Quartz scheduler to register poll jobs at startup.
    /// </summary>
    IReadOnlyList<DeviceInfo> AllDevices { get; }

    /// <summary>
    /// Atomically replaces the device registry with a new set of pre-validated devices.
    /// Builds FrozenDictionary lookups and returns the sets of added and removed IP:Port keys.
    /// DNS resolution, CommunityString extraction, OID resolution, and duplicate detection
    /// are performed by <see cref="SnmpCollector.Services.DeviceWatcherService.ValidateAndBuildDevicesAsync"/>
    /// before calling this method.
    /// </summary>
    /// <param name="devices">The pre-validated device list to store.</param>
    /// <returns>Tuple of added and removed IP:Port identity key sets.</returns>
    Task<(IReadOnlySet<string> Added, IReadOnlySet<string> Removed)> ReloadAsync(List<DeviceInfo> devices);
}
