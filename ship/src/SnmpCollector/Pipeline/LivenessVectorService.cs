using System.Collections.Concurrent;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Thread-safe liveness vector backed by <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// Jobs stamp their key on completion; health checks read stamps to detect staleness.
/// </summary>
public sealed class LivenessVectorService : ILivenessVectorService
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _stamps = new();

    /// <inheritdoc />
    public void Stamp(string jobKey)
    {
        _stamps[jobKey] = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc />
    public DateTimeOffset? GetStamp(string jobKey)
    {
        return _stamps.TryGetValue(jobKey, out var ts) ? ts : null;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, DateTimeOffset> GetAllStamps()
    {
        return _stamps.ToDictionary(kv => kv.Key, kv => kv.Value).AsReadOnly();
    }

    /// <inheritdoc />
    public void Remove(string jobKey)
    {
        _stamps.TryRemove(jobKey, out _);
    }
}
