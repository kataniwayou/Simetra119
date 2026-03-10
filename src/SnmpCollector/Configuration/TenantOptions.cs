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
    /// Metric slots defining which (ip, port, metric_name) tuples this tenant polls.
    /// An empty list is valid (tenant exists but has no poll targets).
    /// </summary>
    public List<MetricSlotOptions> Metrics { get; set; } = [];
}
