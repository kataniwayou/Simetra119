using System.ComponentModel.DataAnnotations;

namespace SnmpCollector.Configuration;

/// <summary>
/// Snapshot job timing configuration. Bound from "SnapshotJob" section.
/// </summary>
public sealed class SnapshotJobOptions
{
    public const string SectionName = "SnapshotJob";

    /// <summary>
    /// Interval between snapshot SET cycles, in seconds.
    /// </summary>
    [Range(1, 300)]
    public int IntervalSeconds { get; set; } = 15;

    /// <summary>
    /// Fraction of <see cref="IntervalSeconds"/> used as the SNMP SET timeout.
    /// Default 0.8 means 15 * 0.8 = 12 seconds timeout.
    /// </summary>
    [Range(0.1, 0.9)]
    public double TimeoutMultiplier { get; set; } = 0.8;
}
