namespace SnmpCollector.Configuration;

/// <summary>
/// Optional min/max bounds for a metric slot. Stored in MetricSlotHolder
/// for future runtime threshold evaluation. Neither Min nor Max is required.
/// </summary>
public sealed class ThresholdOptions
{
    public double? Min { get; set; }
    public double? Max { get; set; }
}
