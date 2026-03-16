using System.Collections.Concurrent;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Thread-safe suppression cache backed by <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// Entries expire lazily -- no background sweep. Dead entries from deleted tenants expire
/// naturally and are overwritten on next access.
/// </summary>
public sealed class SuppressionCache : ISuppressionCache
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _stamps = new();

    /// <inheritdoc />
    public bool TrySuppress(string key, int windowSeconds)
    {
        var now = DateTimeOffset.UtcNow;

        if (_stamps.TryGetValue(key, out var lastStamp)
            && now - lastStamp < TimeSpan.FromSeconds(windowSeconds))
        {
            // Within window -- suppress. Do NOT update the stamp.
            return true;
        }

        // Outside window or first call -- stamp and proceed.
        _stamps[key] = now;
        return false;
    }

    /// <inheritdoc />
    public int Count => _stamps.Count;
}
