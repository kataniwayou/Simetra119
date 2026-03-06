namespace SnmpCollector.Pipeline;

/// <summary>
/// Immutable runtime representation of a monitored device, holding its identity
/// and poll group definitions. Community string is derived at usage time via
/// <see cref="CommunityStringHelper.DeriveFromDeviceName"/>.
/// Built at startup by <see cref="DeviceRegistry"/> from <see cref="Configuration.DevicesOptions"/>.
/// </summary>
/// <param name="Name">Human-readable device name used as 'agent' label and Quartz job identity component.</param>
/// <param name="IpAddress">IPv4 address string of the device.</param>
/// <param name="PollGroups">Metric poll groups for this device, each with its own OID list and interval.</param>
public sealed record DeviceInfo(
    string Name,
    string IpAddress,
    IReadOnlyList<MetricPollInfo> PollGroups);
