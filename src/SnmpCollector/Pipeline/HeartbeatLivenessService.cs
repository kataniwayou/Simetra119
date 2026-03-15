namespace SnmpCollector.Pipeline;

/// <summary>
/// Thread-safe single-value liveness stamp for heartbeat pipeline arrival.
/// Stamped by OtelMetricHandler when the heartbeat OID completes the pipeline.
/// Uses volatile long (UTC ticks) for lock-free single-value read/write.
/// A ticks value of 0 means no heartbeat has been observed since startup.
/// </summary>
public sealed class HeartbeatLivenessService : IHeartbeatLivenessService
{
    private long _lastArrivalTicks; // 0 = never stamped

    public void Stamp()
    {
        Volatile.Write(ref _lastArrivalTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    public DateTimeOffset? LastArrival
    {
        get
        {
            var ticks = Volatile.Read(ref _lastArrivalTicks);
            return ticks == 0L ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }
}
