using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Singleton registry that maps devices by (IP, Port) as primary key and by Name as
/// secondary key for O(1) device lookup. Supports runtime reload via <see cref="ReloadAsync"/>
/// with atomic <see cref="FrozenDictionary{TKey,TValue}"/> swap.
/// </summary>
public sealed class DeviceRegistry : IDeviceRegistry
{
    private readonly ILogger<DeviceRegistry> _logger;
    private volatile FrozenDictionary<string, DeviceInfo> _byIpPort;
    private volatile FrozenDictionary<string, DeviceInfo> _byName;

    /// <summary>
    /// Initializes the registry by building FrozenDictionary lookups from configuration.
    /// For each device:
    /// - IP is normalized to IPv4 via <see cref="IPAddress.MapToIPv4"/>.
    /// - Poll groups are converted to <see cref="MetricPollInfo"/> with their zero-based index.
    /// Throws <see cref="InvalidOperationException"/> if duplicate IP+Port is detected.
    /// </summary>
    /// <param name="devicesOptions">The configured devices to register.</param>
    /// <param name="logger">Logger for structured reload output.</param>
    public DeviceRegistry(IOptions<DevicesOptions> devicesOptions, ILogger<DeviceRegistry> logger)
    {
        _logger = logger;
        var devices = devicesOptions.Value.Devices;

        var byIpPortBuilder = new Dictionary<string, DeviceInfo>(devices.Count, StringComparer.OrdinalIgnoreCase);
        var byNameBuilder = new Dictionary<string, DeviceInfo>(devices.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var d in devices)
        {
            IPAddress ip;
            if (IPAddress.TryParse(d.IpAddress, out var parsed))
            {
                ip = parsed.MapToIPv4();
            }
            else
            {
                // Resolve K8s Service DNS name to IP at startup
                var addresses = Dns.GetHostAddresses(d.IpAddress);
                ip = addresses.First(a => a.AddressFamily == AddressFamily.InterNetwork);
            }

            var pollGroups = d.Polls
                .Select((poll, index) => new MetricPollInfo(
                    PollIndex: index,
                    Oids: poll.MetricNames.AsReadOnly(),
                    IntervalSeconds: poll.IntervalSeconds,
                    TimeoutMultiplier: poll.TimeoutMultiplier))
                .ToList()
                .AsReadOnly();

            var info = new DeviceInfo(d.Name, d.IpAddress, ip.ToString(), d.Port, pollGroups, d.CommunityString);

            var ipPortKey = IpPortKey(info.ConfigAddress, info.Port);
            if (byIpPortBuilder.TryGetValue(ipPortKey, out var existing))
            {
                throw new InvalidOperationException(
                    $"Duplicate address+port {ipPortKey} in device configuration (devices: '{existing.Name}', '{info.Name}')");
            }

            byIpPortBuilder[ipPortKey] = info;
            byNameBuilder[info.Name] = info;
        }

        _byIpPort = byIpPortBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _byName = byNameBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
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
    public async Task<(IReadOnlySet<string> Added, IReadOnlySet<string> Removed)> ReloadAsync(List<DeviceOptions> devices)
    {
        var oldKeys = new HashSet<string>(_byIpPort.Keys, StringComparer.OrdinalIgnoreCase);

        var byIpPortBuilder = new Dictionary<string, DeviceInfo>(devices.Count, StringComparer.OrdinalIgnoreCase);
        var byNameBuilder = new Dictionary<string, DeviceInfo>(devices.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var d in devices)
        {
            IPAddress ip;
            if (IPAddress.TryParse(d.IpAddress, out var parsed))
            {
                ip = parsed.MapToIPv4();
            }
            else
            {
                // Async DNS resolution for K8s Service names
                var addresses = await Dns.GetHostAddressesAsync(d.IpAddress).ConfigureAwait(false);
                ip = addresses.First(a => a.AddressFamily == AddressFamily.InterNetwork);
            }

            var pollGroups = d.Polls
                .Select((poll, index) => new MetricPollInfo(
                    PollIndex: index,
                    Oids: poll.MetricNames.AsReadOnly(),
                    IntervalSeconds: poll.IntervalSeconds,
                    TimeoutMultiplier: poll.TimeoutMultiplier))
                .ToList()
                .AsReadOnly();

            var info = new DeviceInfo(d.Name, d.IpAddress, ip.ToString(), d.Port, pollGroups, d.CommunityString);

            var ipPortKey = IpPortKey(info.ConfigAddress, info.Port);
            if (byIpPortBuilder.TryGetValue(ipPortKey, out var existing))
            {
                throw new InvalidOperationException(
                    $"Duplicate address+port {ipPortKey} in device configuration (devices: '{existing.Name}', '{info.Name}')");
            }

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

        return (added, removed);
    }

    private static string IpPortKey(string ip, int port) => $"{ip}:{port}";
}
