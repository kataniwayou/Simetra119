using System.ComponentModel.DataAnnotations;

namespace SnmpCollector.Configuration;

/// <summary>
/// Heartbeat job timing configuration. Bound from "HeartbeatJob" section.
/// </summary>
public sealed class HeartbeatJobOptions
{
    public const string SectionName = "HeartbeatJob";

    /// <summary>
    /// The heartbeat OID sent in the loopback trap. Single source of truth -- avoids magic strings.
    /// </summary>
    public const string HeartbeatOid = "1.3.6.1.4.1.9999.1.1.1.0";

    /// <summary>
    /// Device name used by HeartbeatJob's loopback trap. Single source of truth for the
    /// "Simetra" device name — all comparisons reference this const.
    /// </summary>
    public const string HeartbeatDeviceName = "Simetra";

    /// <summary>
    /// Compile-time default for heartbeat interval. Used by TenantVectorRegistry to create
    /// the hardcoded heartbeat slot without needing IOptions injection.
    /// </summary>
    public const int DefaultIntervalSeconds = 15;

    /// <summary>
    /// Interval between heartbeat trap sends, in seconds.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int IntervalSeconds { get; set; } = DefaultIntervalSeconds;
}
