namespace SnmpCollector.Pipeline;

/// <summary>
/// Provides access to the tenant vector registry, which holds all tenants grouped by
/// priority order and a routing index for fast fan-out dispatch.
/// <para>
/// Implemented by <see cref="TenantVectorRegistry"/>, registered as a DI singleton.
/// The interface is exposed for testability and injection.
/// </para>
/// </summary>
public interface ITenantVectorRegistry
{
    /// <summary>
    /// Priority-sorted groups of tenants, ascending (lowest priority value = highest priority = first group).
    /// Empty until <see cref="TenantVectorRegistry.Reload"/> is called for the first time.
    /// </summary>
    IReadOnlyList<PriorityGroup> Groups { get; }

    /// <summary>
    /// Total number of distinct tenants currently registered.
    /// Zero until <see cref="TenantVectorRegistry.Reload"/> is called for the first time.
    /// </summary>
    int TenantCount { get; }

    /// <summary>
    /// Total number of metric slot holders across all tenants.
    /// Zero until <see cref="TenantVectorRegistry.Reload"/> is called for the first time.
    /// </summary>
    int SlotCount { get; }

    /// <summary>
    /// Looks up the list of <see cref="MetricSlotHolder"/> instances that match the given
    /// routing key (ip, port, metricName). The lookup is case-insensitive.
    /// </summary>
    /// <param name="ip">IP address of the target device.</param>
    /// <param name="port">SNMP port of the target device.</param>
    /// <param name="metricName">Resolved metric name from OID map.</param>
    /// <param name="holders">
    /// When this method returns true, contains the holders for all tenants that registered
    /// this (ip, port, metricName) combination. Otherwise, null.
    /// </param>
    /// <returns>True if one or more holders were found; false otherwise.</returns>
    bool TryRoute(string ip, int port, string metricName, out IReadOnlyList<MetricSlotHolder> holders);
}
