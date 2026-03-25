namespace SnmpCollector.Configuration;

/// <summary>
/// Configuration for a single monitored device.
/// Nested inside DevicesOptions -- not a standalone IOptions registration.
/// </summary>
public sealed class DeviceOptions
{
    /// <summary>
    /// Full SNMP community string (e.g. 'Simetra.NPB-01').
    /// Device name is derived from this via CommunityStringHelper.TryExtractDeviceName().
    /// Must be unique across all devices and follow the Simetra.{DeviceName} convention.
    /// </summary>
    public string CommunityString { get; set; } = string.Empty;

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
    /// Poll configurations for this device.
    /// Each entry is a separate Quartz job: metric-poll-{deviceName}-{pollIndex}.
    /// </summary>
    public List<PollOptions> Polls { get; set; } = [];
}
