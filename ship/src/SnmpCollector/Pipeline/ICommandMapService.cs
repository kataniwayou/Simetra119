namespace SnmpCollector.Pipeline;

/// <summary>
/// Provides bidirectional OID-to-command-name resolution using the configured command map.
/// OIDs or command names absent from the map return null (no sentinel value).
/// Supports hot-reload: map is swapped atomically when configuration changes.
/// </summary>
public interface ICommandMapService
{
    /// <summary>
    /// Forward-resolves an OID to its command name.
    /// Returns null if the OID is not in the current command map.
    /// </summary>
    /// <param name="oid">The OID string to resolve.</param>
    /// <returns>The configured command name, or null if not found.</returns>
    string? ResolveCommandName(string oid);

    /// <summary>
    /// Reverse-resolves a command name to its OID.
    /// Returns null if the command name is not in the current command map.
    /// </summary>
    /// <param name="commandName">The command name to reverse-resolve (e.g., "obp_set_bypass_L1").</param>
    /// <returns>The OID string, or null if the command name is not found.</returns>
    string? ResolveCommandOid(string commandName);

    /// <summary>
    /// Returns all command names currently in the map.
    /// </summary>
    IReadOnlyCollection<string> GetAllCommandNames();

    /// <summary>
    /// Checks whether a command name exists in the current map.
    /// </summary>
    /// <param name="commandName">The command name to check.</param>
    /// <returns>True if the command name exists in the current map.</returns>
    bool Contains(string commandName);

    /// <summary>
    /// Number of command entries currently in the map.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Atomically replaces the command map with the provided entries.
    /// Computes and logs the diff (added, removed, changed) for observability.
    /// Called by the ConfigMap watcher when configuration changes are detected.
    /// </summary>
    /// <param name="entries">The new OID-to-command-name mapping to swap in.</param>
    void UpdateMap(Dictionary<string, string> entries);
}
