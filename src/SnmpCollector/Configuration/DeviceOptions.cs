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
    /// SNMP port for this device. Defaults to 161 (standard SNMP port).
    /// Must be 1-65535.
    /// </summary>
    public int Port { get; set; } = 161;

    /// <summary>
    /// Optional explicit SNMP community string. If null or empty, falls back to
    /// the Simetra.{Name} convention via CommunityStringHelper.
    /// </summary>
    public string? CommunityString { get; set; }

    /// <summary>
    /// Poll configurations for this device.
    /// Each entry is a separate Quartz job: metric-poll-{deviceName}-{pollIndex}.
    /// </summary>
    public List<PollOptions> Polls { get; set; } = [];
}
