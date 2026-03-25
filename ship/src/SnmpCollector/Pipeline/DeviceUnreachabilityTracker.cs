using System.Collections.Concurrent;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Singleton implementation of <see cref="IDeviceUnreachabilityTracker"/> that tracks
/// consecutive poll failures per device using a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Marks a device unreachable after 3 consecutive failures (hardcoded threshold).
/// Detects both the transition to unreachable and the transition back to healthy.
/// </summary>
public sealed class DeviceUnreachabilityTracker : IDeviceUnreachabilityTracker
{
    // Hardcoded per locked decision from CONTEXT.md: 3 consecutive failures = unreachable
    private readonly int _threshold = 3;

    // StringComparer.OrdinalIgnoreCase: device names are user-configured; case may vary
    private readonly ConcurrentDictionary<string, DeviceState> _state = new(
        StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool RecordFailure(string deviceName)
    {
        var state = _state.GetOrAdd(deviceName, _ => new DeviceState());
        return state.RecordFailure(_threshold);
    }

    /// <inheritdoc />
    public bool RecordSuccess(string deviceName)
    {
        var state = _state.GetOrAdd(deviceName, _ => new DeviceState());
        return state.RecordSuccess();
    }

    /// <inheritdoc />
    public int GetFailureCount(string deviceName)
        => _state.TryGetValue(deviceName, out var state) ? state.Count : 0;

    /// <inheritdoc />
    public bool IsUnreachable(string deviceName)
        => _state.TryGetValue(deviceName, out var state) && state.IsUnreachable;

    // Inner class to avoid ConcurrentDictionary struct-update atomicity issues.
    // With [DisallowConcurrentExecution], the same device's job cannot run simultaneously,
    // so volatile + Interlocked is defensive but correct practice for the shared singleton.
    private sealed class DeviceState
    {
        private volatile int _count;
        private volatile bool _isUnreachable;

        public int Count => _count;
        public bool IsUnreachable => _isUnreachable;

        public bool RecordFailure(int threshold)
        {
            var newCount = Interlocked.Increment(ref _count);
            if (newCount >= threshold && !_isUnreachable)
            {
                _isUnreachable = true;
                return true;  // transition to unreachable
            }
            return false;
        }

        public bool RecordSuccess()
        {
            Interlocked.Exchange(ref _count, 0);
            if (_isUnreachable)
            {
                _isUnreachable = false;
                return true;  // transition to recovered
            }
            return false;
        }
    }
}
