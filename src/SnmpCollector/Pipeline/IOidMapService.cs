namespace SnmpCollector.Pipeline;

/// <summary>
/// Resolves SNMP OID strings to human-readable metric names using the configured OID map.
/// OIDs absent from the map resolve to <see cref="OidMapService.Unknown"/>.
/// Supports hot-reload: map is swapped atomically when appsettings change.
/// </summary>
public interface IOidMapService
{
    /// <summary>
    /// Resolves an OID to its configured metric name.
    /// Returns <see cref="OidMapService.Unknown"/> if the OID is not in the map.
    /// </summary>
    /// <param name="oid">The OID string to resolve (e.g., "1.3.6.1.2.1.25.3.3.1.2").</param>
    /// <returns>The configured metric name, or <see cref="OidMapService.Unknown"/> if not found.</returns>
    string Resolve(string oid);

    /// <summary>
    /// Number of OID entries currently in the map.
    /// </summary>
    int EntryCount { get; }

    /// <summary>
    /// Checks whether a metric name exists as a value in the current OID map.
    /// </summary>
    /// <param name="metricName">The metric name to look up.</param>
    /// <returns>True if the metric name exists in the current map values.</returns>
    bool ContainsMetricName(string metricName);

    /// <summary>
    /// Atomically replaces the OID map with the provided entries.
    /// Computes and logs the diff (added, removed, changed) for observability.
    /// Called by the ConfigMap watcher when configuration changes are detected.
    /// </summary>
    /// <param name="entries">The new OID-to-metric-name mapping to swap in.</param>
    void UpdateMap(Dictionary<string, string> entries);
}
