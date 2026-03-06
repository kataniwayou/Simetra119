namespace SnmpCollector.Configuration;

/// <summary>
/// Site identification configuration. Bound from "Site" section.
/// </summary>
public sealed class SiteOptions
{
    public const string SectionName = "Site";

    /// <summary>
    /// Optional site name identifier (e.g., "site-nyc-01").
    /// Used as the site_name label on OTel metrics when provided.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Pod identity for Kubernetes lease holder identification.
    /// Defaults to HOSTNAME environment variable (the K8s pod name) via PostConfigure
    /// when not explicitly set in configuration. Falls back to Environment.MachineName.
    /// </summary>
    public string? PodIdentity { get; set; }
}
