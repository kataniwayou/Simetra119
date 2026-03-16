using System.ComponentModel.DataAnnotations;

namespace SnmpCollector.Configuration;

/// <summary>
/// Liveness detection configuration. Bound from "Liveness" section.
/// Controls the grace multiplier used by LivenessHealthCheck to determine
/// when a job is considered stale (age > interval * GraceMultiplier).
/// </summary>
public sealed class LivenessOptions
{
    public const string SectionName = "Liveness";

    /// <summary>
    /// Multiplier applied to each job's configured interval to determine staleness threshold.
    /// A job is considered stale after (IntervalSeconds * GraceMultiplier) seconds without completion.
    /// Default 2.0: a 30s poll job is stale after 60s.
    /// </summary>
    [Range(2.0, 5.0)]
    public double GraceMultiplier { get; set; } = 2.0;
}
