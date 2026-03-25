namespace SnmpCollector.Configuration;

/// <summary>
/// Options for a single metric within a poll group.
/// </summary>
public sealed class PollMetricOptions
{
    public string MetricName { get; set; } = string.Empty;
}
