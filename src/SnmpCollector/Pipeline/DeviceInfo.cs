namespace SnmpCollector.Pipeline;

/// <summary>
/// Immutable runtime representation of a monitored device, holding its identity,
/// connection parameters, and poll group definitions.
/// Built at startup by <see cref="DeviceRegistry"/> from <see cref="Configuration.DevicesOptions"/>.
/// </summary>
/// <param name="Name">Derived from CommunityString via CommunityStringHelper.TryExtractDeviceName(). Used as device_name Prometheus label.</param>
/// <param name="ConfigAddress">Raw address from config (DNS name or IP) — used in job keys and registry lookup.</param>
/// <param name="ResolvedIp">Resolved IPv4 address string — used for actual SNMP GET calls.</param>
/// <param name="Port">SNMP port for this device (default 161).</param>
/// <param name="PollGroups">Metric poll groups for this device, each with its own OID list and interval.</param>
/// <param name="CommunityString">Full SNMP community string from config. Used directly for SNMP GET calls.</param>
public sealed record DeviceInfo(
    string Name,
    string ConfigAddress,
    string ResolvedIp,
    int Port,
    IReadOnlyList<MetricPollInfo> PollGroups,
    string CommunityString);
