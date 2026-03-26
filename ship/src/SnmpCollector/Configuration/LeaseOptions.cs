using System.ComponentModel.DataAnnotations;

namespace SnmpCollector.Configuration;

/// <summary>
/// Leader election lease configuration. Bound from "Lease" section.
/// Only used when running inside Kubernetes (K8sLeaseElection).
/// </summary>
public sealed class LeaseOptions
{
    public const string SectionName = "Lease";

    /// <summary>
    /// Lease resource name in the coordination.k8s.io API group.
    /// </summary>
    [Required]
    public required string Name { get; set; } = "snmp-collector-leader";

    /// <summary>
    /// Kubernetes namespace for the lease resource.
    /// </summary>
    [Required]
    public required string Namespace { get; set; } = "default";

    /// <summary>
    /// How often the leader renews its lease, in seconds.
    /// Must be less than DurationSeconds.
    /// </summary>
    [Range(1, 300)]
    public int RenewIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// Total lease duration in seconds. If the leader fails to renew within this period,
    /// other instances may acquire the lease.
    /// </summary>
    [Range(1, 600)]
    public int DurationSeconds { get; set; } = 15;

    /// <summary>
    /// Optional. When set, the pod whose PHYSICAL_HOSTNAME matches this value
    /// is treated as the preferred leader candidate.
    /// Feature is disabled when absent or empty — backward compatible.
    /// Derived heartbeat lease name: "{Name}-preferred".
    /// </summary>
    public string? PreferredNode { get; set; }
}
