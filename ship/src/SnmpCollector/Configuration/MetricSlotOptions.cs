namespace SnmpCollector.Configuration;

/// <summary>
/// Configuration for a single metric slot within a tenant.
/// Identifies a specific (ip, port, resolved_name) tuple for tenant vector routing.
/// </summary>
public sealed class MetricSlotOptions
{
    /// <summary>
    /// IP address of the target device. Must be a valid IPv4 or IPv6 address.
    /// </summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>
    /// SNMP port of the target device. Defaults to 161 (standard SNMP port).
    /// Must be 1-65535.
    /// </summary>
    public int Port { get; set; } = 161;

    /// <summary>
    /// Metric name that must exist in the OID map. Used as the routing key
    /// for fan-out after OID resolution.
    /// </summary>
    public string MetricName { get; set; } = string.Empty;

    /// <summary>
    /// Number of time-series samples to retain for this metric slot.
    /// Default 1 (single latest value, backward compatible).
    /// </summary>
    public int TimeSeriesSize { get; set; } = 1;

    /// <summary>
    /// Poll interval in seconds for this metric slot, used for observability.
    /// Optional; defaults to 0 (unspecified). Stored directly in MetricSlotHolder.
    /// </summary>
    public int IntervalSeconds { get; set; } = 0;

    /// <summary>
    /// Role of this metric slot within the tenant. Must be "Evaluate" or "Resolved".
    /// Validated at load time in Phase 34.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Optional threshold bounds (Min and/or Max) for this metric slot.
    /// Null means no threshold. Stored on MetricSlotHolder at load time
    /// for future runtime evaluation.
    /// </summary>
    public ThresholdOptions? Threshold { get; set; }

    /// <summary>
    /// Resolved grace multiplier from the device's poll group. Not operator-set; populated at tenant load time.
    /// </summary>
    public double GraceMultiplier { get; set; } = 2.0;
}
