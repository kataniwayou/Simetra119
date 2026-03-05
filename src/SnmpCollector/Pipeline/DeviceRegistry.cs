using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Singleton registry that maps normalized IPv4 addresses and device names to
/// <see cref="DeviceInfo"/> for O(1) device lookup. Built once at startup from
/// <see cref="DevicesOptions"/> with community string resolution against the global
/// <see cref="SnmpListenerOptions.CommunityString"/> default.
/// </summary>
public sealed class DeviceRegistry : IDeviceRegistry
{
    private readonly FrozenDictionary<IPAddress, DeviceInfo> _byIp;
    private readonly FrozenDictionary<string, DeviceInfo> _byName;

    /// <summary>
    /// Initializes the registry by building FrozenDictionary lookups from configuration.
    /// For each device:
    /// - IP is normalized to IPv4 via <see cref="IPAddress.MapToIPv4"/>.
    /// - Community string is resolved: per-device override wins; falls back to global default.
    /// - Poll groups are converted to <see cref="MetricPollInfo"/> with their zero-based index.
    /// </summary>
    /// <param name="devicesOptions">The configured devices to register.</param>
    /// <param name="listenerOptions">Global SNMP options providing the community string default.</param>
    public DeviceRegistry(
        IOptions<DevicesOptions> devicesOptions,
        IOptions<SnmpListenerOptions> listenerOptions)
    {
        var devices = devicesOptions.Value.Devices;
        var globalCommunity = listenerOptions.Value.CommunityString;

        var byIpBuilder = new Dictionary<IPAddress, DeviceInfo>(devices.Count);
        var byNameBuilder = new Dictionary<string, DeviceInfo>(devices.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var d in devices)
        {
            var ip = IPAddress.Parse(d.IpAddress).MapToIPv4();

            var community = string.IsNullOrWhiteSpace(d.CommunityString)
                ? globalCommunity
                : d.CommunityString;

            var pollGroups = d.MetricPolls
                .Select((poll, index) => new MetricPollInfo(
                    PollIndex: index,
                    Oids: poll.Oids.AsReadOnly(),
                    IntervalSeconds: poll.IntervalSeconds))
                .ToList()
                .AsReadOnly();

            var info = new DeviceInfo(d.Name, d.IpAddress, community, pollGroups);
            byIpBuilder[ip] = info;
            byNameBuilder[info.Name] = info;
        }

        _byIp = byIpBuilder.ToFrozenDictionary();
        _byName = byNameBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool TryGetDevice(IPAddress senderIp, [NotNullWhen(true)] out DeviceInfo? device)
    {
        return _byIp.TryGetValue(senderIp.MapToIPv4(), out device);
    }

    /// <inheritdoc />
    public bool TryGetDeviceByName(string deviceName, [NotNullWhen(true)] out DeviceInfo? device)
    {
        return _byName.TryGetValue(deviceName, out device);
    }

    /// <inheritdoc />
    public IReadOnlyList<DeviceInfo> AllDevices => _byIp.Values.ToList().AsReadOnly();
}
