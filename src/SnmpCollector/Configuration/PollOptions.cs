namespace SnmpCollector.Configuration;

/// <summary>
/// One poll group for a device. Quartz job identity: metric-poll-{deviceName}-{pollIndex}.
/// All metric names in a poll group are resolved to OIDs and fetched together on the same interval.
/// </summary>
public sealed class PollOptions
{
    /// <summary>
    /// Metric names to poll in this group. Resolved to OIDs at device config load time
    /// via IOidMapService.ResolveToOid.
    /// Must contain at least one entry.
    /// </summary>
    public List<string> MetricNames { get; set; } = [];

    /// <summary>
    /// Polling interval in seconds. Must be greater than zero.
    /// </summary>
    public int IntervalSeconds { get; set; }

    /// <summary>
    /// SNMP GET response timeout as a multiplier of IntervalSeconds (0.1–0.9).
    /// Defaults to 0.8 (80% of interval). Leaves headroom before next trigger fires.
    /// </summary>
    public double TimeoutMultiplier { get; set; } = 0.8;
}
