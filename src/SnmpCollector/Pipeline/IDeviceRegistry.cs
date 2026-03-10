using System.Diagnostics.CodeAnalysis;
using SnmpCollector.Configuration;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Provides O(1) device lookup by IP+Port (primary) or device name (secondary).
/// </summary>
public interface IDeviceRegistry
{
    /// <summary>
    /// Attempts to find a registered device by its IP address and port.
    /// Primary lookup used by poll jobs that receive IP+Port from Quartz JobDataMap.
    /// </summary>
    /// <param name="ipAddress">The device IP address.</param>
    /// <param name="port">The device SNMP port.</param>
    /// <param name="device">The device info if found; null otherwise.</param>
    /// <returns>True if the device was found; false otherwise.</returns>
    bool TryGetByIpPort(string ipAddress, int port, [NotNullWhen(true)] out DeviceInfo? device);

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
    /// Atomically replaces the device registry with a new set of devices.
    /// Performs async DNS resolution for non-IP hostnames and builds new
    /// FrozenDictionary lookups. Returns the sets of added and removed IP:Port identity
    /// keys so callers can update dependent registries (e.g., Quartz jobs, liveness stamps).
    /// </summary>
    /// <param name="devices">The new device list to load.</param>
    /// <returns>Tuple of added and removed IP:Port identity key sets.</returns>
    Task<(IReadOnlySet<string> Added, IReadOnlySet<string> Removed)> ReloadAsync(List<DeviceOptions> devices);
}
