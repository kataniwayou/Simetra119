using System.Collections.Frozen;
using Microsoft.Extensions.Logging;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Singleton service that provides bidirectional OID-to-command-name resolution using
/// volatile <see cref="FrozenDictionary{TKey,TValue}"/> maps that are atomically swapped on reload.
/// No heartbeat seed and no sentinel values -- unknown lookups return null.
/// Callers invoke <see cref="UpdateMap"/> to supply a new map (e.g., from ConfigMap watcher).
/// </summary>
public sealed class CommandMapService : ICommandMapService
{
    private readonly ILogger<CommandMapService> _logger;
    private volatile FrozenDictionary<string, string> _forwardMap;  // OID -> command name
    private volatile FrozenDictionary<string, string> _reverseMap;  // command name -> OID

    /// <summary>
    /// Initializes the service with the provided initial command map entries.
    /// </summary>
    /// <param name="initialEntries">Initial OID-to-command-name mapping.</param>
    /// <param name="logger">Logger for structured hot-reload diff output.</param>
    public CommandMapService(
        Dictionary<string, string> initialEntries,
        ILogger<CommandMapService> logger)
    {
        _logger = logger;
        _forwardMap = initialEntries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _reverseMap = BuildReverseMap(_forwardMap);
    }

    /// <inheritdoc />
    public string? ResolveCommandName(string oid)
    {
        return _forwardMap.TryGetValue(oid, out var name) ? name : null;
    }

    /// <inheritdoc />
    public string? ResolveCommandOid(string commandName)
    {
        return _reverseMap.TryGetValue(commandName, out var oid) ? oid : null;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetAllCommandNames()
    {
        return _reverseMap.Keys;
    }

    /// <inheritdoc />
    public bool Contains(string commandName)
    {
        return _reverseMap.ContainsKey(commandName);
    }

    /// <inheritdoc />
    public int Count => _forwardMap.Count;

    /// <inheritdoc />
    public void UpdateMap(Dictionary<string, string> entries)
    {
        var oldMap = _forwardMap;
        var newForward = entries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        // Compute diff for structured logging
        var added = newForward.Keys.Except(oldMap.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        var removed = oldMap.Keys.Except(newForward.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        var changed = newForward.Keys
            .Intersect(oldMap.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(k => oldMap[k] != newForward[k])
            .ToList();

        // Atomic swap -- volatile write ensures all readers see the new maps immediately
        _forwardMap = newForward;
        _reverseMap = BuildReverseMap(newForward);

        _logger.LogInformation(
            "CommandMap hot-reloaded: {EntryCount} entries total, +{Added} added, -{Removed} removed, ~{Changed} changed",
            newForward.Count,
            added.Count,
            removed.Count,
            changed.Count);

        foreach (var oid in added)
            _logger.LogInformation("CommandMap added: {Oid} -> {CommandName}", oid, newForward[oid]);

        foreach (var oid in removed)
            _logger.LogInformation("CommandMap removed: {Oid} (was {CommandName})", oid, oldMap[oid]);

        foreach (var oid in changed)
            _logger.LogInformation("CommandMap changed: {Oid} {OldName} -> {NewName}", oid, oldMap[oid], newForward[oid]);
    }

    private static FrozenDictionary<string, string> BuildReverseMap(FrozenDictionary<string, string> forwardMap)
    {
        return forwardMap
            .Select(kv => new KeyValuePair<string, string>(kv.Value, kv.Key))
            .ToFrozenDictionary(StringComparer.Ordinal);
    }
}
