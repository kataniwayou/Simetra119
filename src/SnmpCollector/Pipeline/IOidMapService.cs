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
}
