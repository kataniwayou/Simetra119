namespace SnmpCollector.Pipeline;

/// <summary>
/// Immutable runtime representation of one metric poll group for a device.
/// Each poll group has its own OID list and polling interval, and maps to a single Quartz job.
/// </summary>
/// <param name="PollIndex">Zero-based index of this poll group within the device's MetricPolls list.</param>
/// <param name="Oids">OID strings to fetch together in a single SNMP GET request.</param>
/// <param name="IntervalSeconds">Polling interval in seconds for this group.</param>
public sealed record MetricPollInfo(
    int PollIndex,
    IReadOnlyList<string> Oids,
    int IntervalSeconds)
{
    /// <summary>
    /// Returns the Quartz job key for this poll group.
    /// Pattern: "metric-poll-{deviceName}-{pollIndex}"
    /// </summary>
    /// <param name="deviceName">The device name this poll group belongs to.</param>
    public string JobKey(string deviceName) => $"metric-poll-{deviceName}-{PollIndex}";
}
