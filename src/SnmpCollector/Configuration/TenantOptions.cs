namespace SnmpCollector.Configuration;

/// <summary>
/// Configuration for a single tenant within the tenant vector.
/// Each tenant has a priority for scheduling and a list of metric slots.
/// </summary>
public sealed class TenantOptions
{
    /// <summary>
    /// Scheduling priority for this tenant. Lower values = higher priority.
    /// Any integer is valid (no range constraint).
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Metric slots defining which (ip, port, resolved_name) tuples this tenant polls.
    /// An empty list is valid (tenant exists but has no poll targets).
    /// </summary>
    public List<MetricSlotOptions> Metrics { get; set; } = [];

    /// <summary>
    /// Optional human-readable tenant name. When present, used in log context
    /// instead of the auto-generated "tenant-{index}" identifier.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Command slots defining SNMP SET commands for this tenant.
    /// An empty list (or absent from JSON) is valid -- tenant has no command targets.
    /// </summary>
    public List<CommandSlotOptions> Commands { get; set; } = [];

    /// <summary>
    /// Duration of the command suppression window in seconds. Default: 60.
    /// </summary>
    public int SuppressionWindowSeconds { get; set; } = 60;
}
