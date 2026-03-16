namespace SnmpCollector.Pipeline;

/// <summary>
/// Thread-safe cache that suppresses duplicate operations within a configurable time window.
/// Used by SnapshotJob to prevent duplicate SET commands for the same target within the
/// suppression window.
/// </summary>
public interface ISuppressionCache
{
    /// <summary>
    /// Checks whether the given key should be suppressed. Returns <c>true</c> if the key
    /// was seen within the last <paramref name="windowSeconds"/> seconds (caller should skip).
    /// Returns <c>false</c> if the key is new or the window has expired (caller should proceed).
    /// <para>
    /// Only the <c>false</c> (proceed) path stamps the timestamp. Suppressed calls do NOT
    /// update the timestamp, so the window does not extend on repeated suppressed calls.
    /// </para>
    /// </summary>
    /// <param name="key">Unique identifier for the operation (e.g. "{Ip}:{Port}:{CommandName}").</param>
    /// <param name="windowSeconds">Duration of the suppression window in seconds.</param>
    /// <returns><c>true</c> if suppressed (skip); <c>false</c> if not suppressed (proceed).</returns>
    bool TrySuppress(string key, int windowSeconds);

    /// <summary>
    /// Number of entries currently tracked in the cache. Diagnostic property.
    /// </summary>
    int Count { get; }
}
