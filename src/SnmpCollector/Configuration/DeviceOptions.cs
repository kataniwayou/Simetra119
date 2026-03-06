namespace SnmpCollector.Configuration;

/// <summary>
/// Configuration for a single monitored device.
/// Nested inside DevicesOptions -- not a standalone IOptions registration.
/// </summary>
public sealed class DeviceOptions
{
    /// <summary>
    /// Human-readable device name used as 'agent' label and Quartz job identity component.
    /// Must be unique across all devices.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// IP address for SNMP polling and trap source matching.
    /// Must be unique across all devices.
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// Metric polling configurations for this device.
    /// Each entry is a separate Quartz job: metric-poll-{deviceName}-{pollIndex}.
    /// </summary>
    public List<MetricPollOptions> MetricPolls { get; set; } = [];
}
