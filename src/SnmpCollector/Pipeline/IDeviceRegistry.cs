using System.Diagnostics.CodeAnalysis;
using System.Net;
using SnmpCollector.Configuration;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Provides O(1) device lookup by IPv4 address (for incoming SNMP traps) and by device
/// name (for poll jobs that receive the device name from Quartz JobDataMap).
/// </summary>
public interface IDeviceRegistry
{
    /// <summary>
    /// Attempts to find a registered device by its sender IP address.
    /// The IP is normalized to IPv4 before lookup.
    /// Used by the trap listener path to resolve the sending agent.
    /// </summary>
    /// <param name="senderIp">The IP address of the trap sender.</param>
    /// <param name="device">The device info if found; null otherwise.</param>
    /// <returns>True if the device was found; false otherwise.</returns>
    bool TryGetDevice(IPAddress senderIp, [NotNullWhen(true)] out DeviceInfo? device);

    /// <summary>
    /// Attempts to find a registered device by its configured name.
    /// Used by poll jobs that know the device name from their Quartz JobDataMap.
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
    /// FrozenDictionary lookups. Returns the sets of added and removed device names
    /// so callers can update dependent registries (e.g., Quartz jobs, liveness stamps).
    /// </summary>
    /// <param name="devices">The new device list to load.</param>
    /// <returns>Tuple of added and removed device name sets.</returns>
    Task<(IReadOnlySet<string> Added, IReadOnlySet<string> Removed)> ReloadAsync(List<DeviceOptions> devices);
}
