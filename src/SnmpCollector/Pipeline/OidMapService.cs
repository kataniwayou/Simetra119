using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Singleton service that resolves OID strings to metric names using a volatile
/// <see cref="FrozenDictionary{TKey,TValue}"/> that is atomically swapped on appsettings change.
/// Hot-reload via <see cref="IOptionsMonitor{TOptions}.OnChange"/> -- no restart required.
/// </summary>
public sealed class OidMapService : IOidMapService, IDisposable
{
    /// <summary>
    /// Metric name returned when an OID is not present in the map.
    /// Visible in Grafana as a discovery mechanism for unmapped OIDs.
    /// </summary>
    public const string Unknown = "Unknown";

    private readonly ILogger<OidMapService> _logger;
    private readonly IDisposable? _changeToken;
    private volatile FrozenDictionary<string, string> _map;

    /// <summary>
    /// Initializes the service and subscribes to appsettings change notifications.
    /// </summary>
    /// <param name="monitor">Options monitor providing initial map and change callbacks.</param>
    /// <param name="logger">Logger for structured hot-reload diff output.</param>
    public OidMapService(
        IOptionsMonitor<OidMapOptions> monitor,
        ILogger<OidMapService> logger)
    {
        _logger = logger;
        _map = BuildFrozenMap(monitor.CurrentValue.Entries);
        _changeToken = monitor.OnChange(OnOidMapChanged);

        _logger.LogInformation(
            "OidMapService initialized with {EntryCount} entries",
            _map.Count);
    }

    /// <inheritdoc />
    public string Resolve(string oid)
    {
        return _map.TryGetValue(oid, out var name) ? name : Unknown;
    }

    /// <inheritdoc />
    public int EntryCount => _map.Count;

    /// <inheritdoc />
    public void Dispose()
    {
        _changeToken?.Dispose();
    }

    private void OnOidMapChanged(OidMapOptions newOptions, string? _)
    {
        var oldMap = _map;
        var newMap = BuildFrozenMap(newOptions.Entries);

        // Compute diff for structured logging
        var added = newMap.Keys.Except(oldMap.Keys).ToList();
        var removed = oldMap.Keys.Except(newMap.Keys).ToList();
        var changed = newMap.Keys
            .Intersect(oldMap.Keys)
            .Where(k => oldMap[k] != newMap[k])
            .ToList();

        // Atomic swap -- volatile write ensures all readers see the new map immediately
        _map = newMap;

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

    private static FrozenDictionary<string, string> BuildFrozenMap(Dictionary<string, string> entries)
    {
        return entries.ToFrozenDictionary();
    }
}
