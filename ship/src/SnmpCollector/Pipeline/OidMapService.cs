using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using SnmpCollector.Configuration;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Singleton service that resolves OID strings to metric names using a volatile
/// <see cref="FrozenDictionary{TKey,TValue}"/> that is atomically swapped on reload.
/// Callers invoke <see cref="UpdateMap"/> to supply a new map (e.g., from ConfigMap watcher).
/// </summary>
public sealed class OidMapService : IOidMapService
{
    /// <summary>
    /// Metric name returned when an OID is not present in the map.
    /// Visible in Grafana as a discovery mechanism for unmapped OIDs.
    /// </summary>
    public const string Unknown = "Unknown";

    private readonly ILogger<OidMapService> _logger;
    private volatile FrozenDictionary<string, string> _map;
    private volatile FrozenSet<string> _metricNames = FrozenSet<string>.Empty;
    private volatile FrozenDictionary<string, string> _reverseMap = FrozenDictionary<string, string>.Empty;

    /// <summary>
    /// Initializes the service with the provided initial OID map entries.
    /// </summary>
    /// <param name="initialEntries">Initial OID-to-metric-name mapping.</param>
    /// <param name="logger">Logger for structured hot-reload diff output.</param>
    public OidMapService(
        Dictionary<string, string> initialEntries,
        ILogger<OidMapService> logger)
    {
        _logger = logger;
        var seeded = MergeWithHeartbeatSeed(initialEntries);
        _map = BuildFrozenMap(seeded);
        _metricNames = _map.Values.ToFrozenSet();
        _reverseMap = BuildReverseMap(_map);
    }

    /// <inheritdoc />
    public string Resolve(string oid)
    {
        return _map.TryGetValue(oid, out var name) ? name : Unknown;
    }

    /// <inheritdoc />
    public int EntryCount => _map.Count;

    /// <inheritdoc />
    public bool ContainsMetricName(string metricName) => _metricNames.Contains(metricName);

    /// <inheritdoc />
    public string? ResolveToOid(string metricName)
    {
        return _reverseMap.TryGetValue(metricName, out var oid) ? oid : null;
    }

    /// <inheritdoc />
    public void UpdateMap(Dictionary<string, string> entries)
    {
        var oldMap = _map;
        var seeded = MergeWithHeartbeatSeed(entries);
        var newMap = BuildFrozenMap(seeded);

        // Compute diff for structured logging
        var added = newMap.Keys.Except(oldMap.Keys).ToList();
        var removed = oldMap.Keys.Except(newMap.Keys).ToList();
        var changed = newMap.Keys
            .Intersect(oldMap.Keys)
            .Where(k => oldMap[k] != newMap[k])
            .ToList();

        // Atomic swap -- volatile write ensures all readers see the new map immediately
        _map = newMap;
        _metricNames = newMap.Values.ToFrozenSet();
        _reverseMap = BuildReverseMap(newMap);

        _logger.LogInformation(
            "OidMap hot-reloaded: {EntryCount} entries total, +{Added} added, -{Removed} removed, ~{Changed} changed",
            newMap.Count,
            added.Count,
            removed.Count,
            changed.Count);

        foreach (var oid in added)
            _logger.LogInformation("OidMap added: {Oid} -> {MetricName}", oid, newMap[oid]);

        foreach (var oid in removed)
            _logger.LogInformation("OidMap removed: {Oid} (was {MetricName})", oid, oldMap[oid]);

        foreach (var oid in changed)
            _logger.LogInformation("OidMap changed: {Oid} {OldName} -> {NewName}", oid, oldMap[oid], newMap[oid]);
    }

    private static Dictionary<string, string> MergeWithHeartbeatSeed(Dictionary<string, string> entries)
    {
        var merged = new Dictionary<string, string>(entries, StringComparer.OrdinalIgnoreCase);
        merged[SnmpHeartbeatJobOptions.HeartbeatOid] = "Heartbeat";
        return merged;
    }

    private static FrozenDictionary<string, string> BuildReverseMap(FrozenDictionary<string, string> forwardMap)
    {
        return forwardMap
            .Select(kv => new KeyValuePair<string, string>(kv.Value, kv.Key))
            .ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static FrozenDictionary<string, string> BuildFrozenMap(Dictionary<string, string> entries)
    {
        return entries.ToFrozenDictionary();
    }
}
