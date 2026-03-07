using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Singleton registry that maps normalized IPv4 addresses and device names to
/// <see cref="DeviceInfo"/> for O(1) device lookup. Built once at startup from
/// <see cref="DevicesOptions"/>.
/// </summary>
public sealed class DeviceRegistry : IDeviceRegistry
{
    private readonly FrozenDictionary<IPAddress, DeviceInfo> _byIp;
    private readonly FrozenDictionary<string, DeviceInfo> _byName;

    /// <summary>
    /// Initializes the registry by building FrozenDictionary lookups from configuration.
    /// For each device:
    /// - IP is normalized to IPv4 via <see cref="IPAddress.MapToIPv4"/>.
    /// - Poll groups are converted to <see cref="MetricPollInfo"/> with their zero-based index.
    /// </summary>
    /// <param name="devicesOptions">The configured devices to register.</param>
    public DeviceRegistry(IOptions<DevicesOptions> devicesOptions)
    {
        var devices = devicesOptions.Value.Devices;

        var byIpBuilder = new Dictionary<IPAddress, DeviceInfo>(devices.Count);
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

            var pollGroups = d.MetricPolls
                .Select((poll, index) => new MetricPollInfo(
                    PollIndex: index,
                    Oids: poll.Oids.AsReadOnly(),
                    IntervalSeconds: poll.IntervalSeconds))
                .ToList()
                .AsReadOnly();

            var info = new DeviceInfo(d.Name, ip.ToString(), d.Port, pollGroups, d.CommunityString);
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
