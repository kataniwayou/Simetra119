namespace SnmpCollector.Pipeline;

/// <summary>
/// Immutable runtime representation of one metric poll group for a device.
/// Each poll group has its own OID list and polling interval, and maps to a single Quartz job.
/// </summary>
/// <param name="PollIndex">Zero-based index of this poll group within the device's Polls list.</param>
/// <param name="Oids">OID strings to fetch together in a single SNMP GET request.</param>
/// <param name="IntervalSeconds">Polling interval in seconds for this group.</param>
/// <param name="TimeoutMultiplier">SNMP GET response timeout as multiplier of interval (0.1–0.9, default 0.8).</param>
public sealed record MetricPollInfo(
    int PollIndex,
    IReadOnlyList<string> Oids,
    int IntervalSeconds,
    double TimeoutMultiplier = 0.8)
{
    /// <summary>
    /// Returns the Quartz job key for this poll group.
    /// Pattern: "metric-poll-{configAddress}_{port}-{pollIndex}"
    /// Uses the raw config address (DNS or IP) so operators can correlate to ConfigMap entries.
    /// </summary>
    /// <param name="configAddress">The device address as configured (DNS name or IP).</param>
    /// <param name="port">The device SNMP port this poll group belongs to.</param>
    public string JobKey(string configAddress, int port) => $"metric-poll-{configAddress}_{port}-{PollIndex}";
}
