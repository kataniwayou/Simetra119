using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Singleton registry that maps devices by (IP, Port) as primary key and by Name as
/// secondary key for O(1) device lookup. Supports runtime reload via <see cref="ReloadAsync"/>
/// with atomic <see cref="FrozenDictionary{TKey,TValue}"/> swap.
/// <para>
/// This is a pure data store. All validation (CommunityString extraction, DNS resolution,
/// OID resolution, duplicate detection) is handled by
/// <see cref="SnmpCollector.Services.DeviceWatcherService.ValidateAndBuildDevicesAsync"/>
/// before calling <see cref="ReloadAsync"/>.
/// </para>
/// </summary>
public sealed class DeviceRegistry : IDeviceRegistry
{
    private readonly ILogger<DeviceRegistry> _logger;
    private volatile FrozenDictionary<string, DeviceInfo> _byIpPort;
    private volatile FrozenDictionary<string, DeviceInfo> _byName;

    /// <summary>
    /// Initializes an empty registry. Devices are loaded via <see cref="ReloadAsync"/>.
    /// </summary>
    /// <param name="logger">Logger for structured reload output.</param>
    public DeviceRegistry(ILogger<DeviceRegistry> logger)
    {
        _logger = logger;
        _byIpPort = FrozenDictionary<string, DeviceInfo>.Empty;
        _byName = FrozenDictionary<string, DeviceInfo>.Empty;
    }

    /// <inheritdoc />
    public bool TryGetByIpPort(string ipAddress, int port, [NotNullWhen(true)] out DeviceInfo? device)
    {
        return _byIpPort.TryGetValue(IpPortKey(ipAddress, port), out device);
    }

    /// <inheritdoc />
    public bool TryGetDeviceByName(string deviceName, [NotNullWhen(true)] out DeviceInfo? device)
    {
        return _byName.TryGetValue(deviceName, out device);
    }

    /// <inheritdoc />
    public IReadOnlyList<DeviceInfo> AllDevices => _byIpPort.Values.ToList().AsReadOnly();

    /// <inheritdoc />
    public Task<(IReadOnlySet<string> Added, IReadOnlySet<string> Removed)> ReloadAsync(List<DeviceInfo> devices)
    {
        var oldKeys = new HashSet<string>(_byIpPort.Keys, StringComparer.OrdinalIgnoreCase);

        var byIpPortBuilder = new Dictionary<string, DeviceInfo>(devices.Count, StringComparer.OrdinalIgnoreCase);
        var byNameBuilder = new Dictionary<string, DeviceInfo>(devices.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var info in devices)
        {
            var ipPortKey = IpPortKey(info.ConfigAddress, info.Port);
            byIpPortBuilder[ipPortKey] = info;
            byNameBuilder[info.Name] = info;
        }

        var newByIpPort = byIpPortBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        var newByName = byNameBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        // Atomic swap -- volatile write ensures all readers see the new dictionaries
        _byIpPort = newByIpPort;
        _byName = newByName;

        var newKeys = new HashSet<string>(newByIpPort.Keys, StringComparer.OrdinalIgnoreCase);
        var added = new HashSet<string>(newKeys.Except(oldKeys), StringComparer.OrdinalIgnoreCase);
        var removed = new HashSet<string>(oldKeys.Except(newKeys), StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation(
            "DeviceRegistry reloaded: {DeviceCount} devices, +{Added} added, -{Removed} removed",
            newByIpPort.Count,
            added.Count,
            removed.Count);

        return Task.FromResult<(IReadOnlySet<string>, IReadOnlySet<string>)>((added, removed));
    }

    private static string IpPortKey(string ip, int port) => $"{ip}:{port}";
}
