namespace SnmpCollector.Configuration;

/// <summary>
/// One poll group for a device. Quartz job identity: metric-poll-{deviceName}-{pollIndex}.
/// All OIDs in a poll group are fetched together on the same interval.
/// </summary>
public sealed class MetricPollOptions
{
    /// <summary>
    /// OID strings to poll in this group (e.g., "1.3.6.1.2.1.25.3.3.1.2.1").
    /// Must contain at least one entry.
    /// </summary>
    public List<string> Oids { get; set; } = [];

    /// <summary>
    /// Polling interval in seconds. Must be greater than zero.
    /// </summary>
    public int IntervalSeconds { get; set; }

    /// <summary>
    /// SNMP GET response timeout as a multiplier of IntervalSeconds (0.1–0.99).
    /// Defaults to 0.8 (80% of interval). Leaves headroom before next trigger fires.
    /// </summary>
    public double TimeoutMultiplier { get; set; } = 0.8;
}
