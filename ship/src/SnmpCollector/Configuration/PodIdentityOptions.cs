namespace SnmpCollector.Configuration;

/// <summary>
/// Pod identity configuration. Bound from "PodIdentity" section.
/// </summary>
public sealed class PodIdentityOptions
{
    public const string SectionName = "PodIdentity";

    /// <summary>
    /// Pod identity for Kubernetes lease holder identification.
    /// Defaults to HOSTNAME environment variable (the K8s pod name) via PostConfigure
    /// when not explicitly set in configuration. Falls back to Environment.MachineName.
    /// </summary>
    public string? PodIdentity { get; set; }
}
