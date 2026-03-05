using System.ComponentModel.DataAnnotations;

namespace SnmpCollector.Configuration;

/// <summary>
/// Site identification configuration. Bound from "Site" section.
/// </summary>
public sealed class SiteOptions
{
    public const string SectionName = "Site";

    /// <summary>
    /// Unique site name identifier (e.g., "site-nyc-01"). Required.
    /// </summary>
    [Required]
    public required string Name { get; set; }

    /// <summary>
    /// Role of this collector instance (e.g., "standalone", "leader", "follower").
    /// Used by the formatter and enrichment processor to tag telemetry.
    /// Defaults to "standalone".
    /// </summary>
    public string Role { get; set; } = "standalone";

    /// <summary>
    /// Pod identity for Kubernetes lease holder identification.
    /// Defaults to HOSTNAME environment variable (the K8s pod name) via PostConfigure
    /// when not explicitly set in configuration. Falls back to Environment.MachineName.
    /// </summary>
    public string? PodIdentity { get; set; }
}
