namespace SnmpCollector.Telemetry;

/// <summary>
/// Contract between the preferred-leader heartbeat service (writer) and
/// K8sLeaseElection gate consumers (reader).
/// Implementations MUST be thread-safe: <see cref="IsPreferredStampFresh"/> is read on
/// the leader-election hot path.
/// </summary>
public interface IPreferredStampReader
{
    /// <summary>
    /// Returns true when a fresh heartbeat stamp from the preferred pod has been
    /// observed recently. Written by the heartbeat service; read by election gates.
    /// </summary>
    bool IsPreferredStampFresh { get; }
}
