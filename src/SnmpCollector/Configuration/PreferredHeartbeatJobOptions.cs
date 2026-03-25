using System.ComponentModel.DataAnnotations;

namespace SnmpCollector.Configuration;

/// <summary>
/// Preferred heartbeat job timing configuration. Bound from "PreferredHeartbeatJob" section.
/// Controls the Quartz job cadence for reading (and writing) the heartbeat lease.
/// </summary>
public sealed class PreferredHeartbeatJobOptions
{
    public const string SectionName = "PreferredHeartbeatJob";

    /// <summary>
    /// Interval between heartbeat lease read/write cycles, in seconds.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int IntervalSeconds { get; set; } = 15;
}
