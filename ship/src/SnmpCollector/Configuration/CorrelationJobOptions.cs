using System.ComponentModel.DataAnnotations;

namespace SnmpCollector.Configuration;

/// <summary>
/// Correlation job timing configuration. Bound from "CorrelationJob" section.
/// </summary>
public sealed class CorrelationJobOptions
{
    public const string SectionName = "CorrelationJob";

    /// <summary>
    /// Interval between correlation sweeps, in seconds.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int IntervalSeconds { get; set; } = 30;
}
