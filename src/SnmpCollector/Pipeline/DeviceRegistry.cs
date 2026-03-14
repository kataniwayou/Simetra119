using System.Collections.Frozen;
using System.Collections.ObjectModel;
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
    private readonly IOidMapService _oidMapService;
    private volatile FrozenDictionary<string, DeviceInfo> _byIpPort;
    private volatile FrozenDictionary<string, DeviceInfo> _byName;

    /// <summary>
    /// Initializes the registry by building FrozenDictionary lookups from configuration.
    /// For each device:
    /// - CommunityString is parsed via <see cref="CommunityStringHelper.TryExtractDeviceName"/> to derive the short device name.
    /// - IP is normalized to IPv4 via <see cref="IPAddress.MapToIPv4"/>.
    /// - Poll groups resolve MetricNames to OIDs via <see cref="IOidMapService.ResolveToOid"/>.
    /// - Unresolvable metric names are logged as warnings and excluded from the poll group.
    /// - Poll groups with zero resolved OIDs are excluded from PollGroups (logged as Warning); device still registers for traps.
    /// - Devices with invalid CommunityString (not following Simetra.{DeviceName} convention) are skipped with an error log.
    /// - Duplicate IP+Port devices are skipped with an error log (no throw).
    /// - Duplicate CommunityString across devices with different IP+Port logs a Warning; both devices load normally.
    /// </summary>
    /// <param name="devicesOptions">The configured devices to register.</param>
    /// <param name="oidMapService">Service for resolving metric names to OID strings.</param>
    /// <param name="logger">Logger for structured reload output.</param>
    public DeviceRegistry(IOptions<DevicesOptions> devicesOptions, IOidMapService oidMapService, ILogger<DeviceRegistry> logger)
    {
        _logger = logger;
        _oidMapService = oidMapService ?? throw new ArgumentNullException(nameof(oidMapService));
        var devices = devicesOptions.Value.Devices;

        var byIpPortBuilder = new Dictionary<string, DeviceInfo>(devices.Count, StringComparer.OrdinalIgnoreCase);
        var byNameBuilder = new Dictionary<string, DeviceInfo>(devices.Count, StringComparer.OrdinalIgnoreCase);
        var seenCommunityStrings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var d in devices)
        {
            if (!CommunityStringHelper.TryExtractDeviceName(d.CommunityString, out var deviceName))
            {
                _logger.LogError(
                    "Device at {IpAddress}:{Port} has invalid CommunityString '{CommunityString}' -- skipping",
                    d.IpAddress, d.Port, d.CommunityString);
                continue;
            }

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

            var pollGroups = BuildPollGroups(d.Polls, deviceName);

            var info = new DeviceInfo(deviceName, d.IpAddress, ip.ToString(), d.Port, pollGroups, d.CommunityString);

            var ipPortKey = IpPortKey(info.ConfigAddress, info.Port);
            if (byIpPortBuilder.TryGetValue(ipPortKey, out var existing))
            {
                _logger.LogError(
                    "Device at {IpAddress}:{Port} (CommunityString '{CommunityString}') is a duplicate of existing device '{ExistingName}' -- skipping",
                    d.IpAddress, d.Port, d.CommunityString, existing.Name);
                continue;
            }

            if (seenCommunityStrings.TryGetValue(d.CommunityString, out var priorName))
            {
                _logger.LogWarning(
                    "Device '{DeviceName}' at {IpAddress}:{Port} has CommunityString '{CommunityString}' also used by device '{PriorDevice}' -- both loaded (different IP+Port)",
                    deviceName, d.IpAddress, d.Port, d.CommunityString, priorName);
            }
            seenCommunityStrings.TryAdd(d.CommunityString, deviceName);

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
        var seenCommunityStrings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var d in devices)
        {
            if (!CommunityStringHelper.TryExtractDeviceName(d.CommunityString, out var deviceName))
            {
                _logger.LogError(
                    "Device at {IpAddress}:{Port} has invalid CommunityString '{CommunityString}' -- skipping",
                    d.IpAddress, d.Port, d.CommunityString);
                continue;
            }

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

            var pollGroups = BuildPollGroups(d.Polls, deviceName);

            var info = new DeviceInfo(deviceName, d.IpAddress, ip.ToString(), d.Port, pollGroups, d.CommunityString);

            var ipPortKey = IpPortKey(info.ConfigAddress, info.Port);
            if (byIpPortBuilder.TryGetValue(ipPortKey, out var existing))
            {
                _logger.LogError(
                    "Device at {IpAddress}:{Port} (CommunityString '{CommunityString}') is a duplicate of existing device '{ExistingName}' -- skipping",
                    d.IpAddress, d.Port, d.CommunityString, existing.Name);
                continue;
            }

            if (seenCommunityStrings.TryGetValue(d.CommunityString, out var priorName))
            {
                _logger.LogWarning(
                    "Device '{DeviceName}' at {IpAddress}:{Port} has CommunityString '{CommunityString}' also used by device '{PriorDevice}' -- both loaded (different IP+Port)",
                    deviceName, d.IpAddress, d.Port, d.CommunityString, priorName);
            }
            seenCommunityStrings.TryAdd(d.CommunityString, deviceName);

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

    /// <summary>
    /// Resolves MetricNames in each poll group to OIDs via <see cref="IOidMapService.ResolveToOid"/>.
    /// Unresolvable names are logged as warnings and excluded. Resolution summary is always logged
    /// per poll group for reload diff visibility. Poll groups with zero resolved OIDs are excluded
    /// entirely (logged as Warning); devices with all-zero-OID poll groups are still registered for
    /// trap reception.
    /// </summary>
    private ReadOnlyCollection<MetricPollInfo> BuildPollGroups(List<PollOptions> polls, string deviceName)
    {
        var result = new List<MetricPollInfo>();
        for (var index = 0; index < polls.Count; index++)
        {
            var poll = polls[index];
            var resolvedOids = new List<string>();
            var unresolvedNames = new List<string>();

            foreach (var name in poll.MetricNames)
            {
                var oid = _oidMapService.ResolveToOid(name);
                if (oid is not null)
                    resolvedOids.Add(oid);
                else
                {
                    unresolvedNames.Add(name);
                    _logger.LogWarning(
                        "MetricName '{MetricName}' on device '{DeviceName}' poll {PollIndex} not found in OID map -- skipping",
                        name, deviceName, index);
                }
            }

            _logger.LogInformation(
                "Device '{DeviceName}' poll {PollIndex}: resolved {ResolvedCount}/{TotalCount} metric names{UnresolvedDetail}",
                deviceName, index, resolvedOids.Count, poll.MetricNames.Count,
                unresolvedNames.Count > 0 ? $"; unresolved: [{string.Join(", ", unresolvedNames)}]" : "");

            if (resolvedOids.Count == 0)
            {
                _logger.LogWarning(
                    "Device '{DeviceName}' poll {PollIndex} has zero resolved OIDs -- skipping job registration",
                    deviceName, index);
                continue;
            }

            result.Add(new MetricPollInfo(
                PollIndex: index,
                Oids: resolvedOids.AsReadOnly(),
                IntervalSeconds: poll.IntervalSeconds,
                TimeoutMultiplier: poll.TimeoutMultiplier));
        }
        return result.AsReadOnly();
    }

    private static string IpPortKey(string ip, int port) => $"{ip}:{port}";
}
