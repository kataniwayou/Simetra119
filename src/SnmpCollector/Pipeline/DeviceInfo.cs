namespace SnmpCollector.Pipeline;

/// <summary>
/// Immutable runtime representation of a monitored device, holding its identity,
/// connection parameters, and poll group definitions.
/// Built at startup by <see cref="DeviceRegistry"/> from <see cref="Configuration.DevicesOptions"/>.
/// </summary>
/// <param name="Name">Human-readable device name used as 'agent' label and Quartz job identity component.</param>
/// <param name="IpAddress">IPv4 address string of the device.</param>
/// <param name="Port">SNMP port for this device (default 161).</param>
/// <param name="PollGroups">Metric poll groups for this device, each with its own OID list and interval.</param>
/// <param name="CommunityString">Optional explicit SNMP community string override. Null falls back to convention.</param>
public sealed record DeviceInfo(
    string Name,
    string IpAddress,
    int Port,
    IReadOnlyList<MetricPollInfo> PollGroups,
    string? CommunityString = null);
