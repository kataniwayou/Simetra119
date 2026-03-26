namespace SnmpCollector.Pipeline;

/// <summary>
/// Tracks when the heartbeat trap last completed the full MediatR pipeline.
/// Stamped by OtelMetricHandler when DeviceName == SnmpHeartbeatJobOptions.HeartbeatDeviceName.
/// Read by LivenessHealthCheck to detect a silent pipeline (channel blocked, consumer crashed, etc.).
/// </summary>
public interface IHeartbeatLivenessService
{
    /// <summary>Records the current UTC time as the last heartbeat pipeline arrival.</summary>
    void Stamp();

    /// <summary>
    /// The last time a heartbeat completed the pipeline, or null if no heartbeat
    /// has been observed since startup.
    /// </summary>
    DateTimeOffset? LastArrival { get; }
}
