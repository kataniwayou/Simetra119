namespace SnmpCollector.Telemetry;

/// <summary>
/// Abstraction for leader election status. AlwaysLeaderElection (local dev / non-K8s) always
/// reports leader. K8sLeaseElection (Plan 02) provides the real distributed lease implementation.
/// </summary>
public interface ILeaderElection
{
    /// <summary>
    /// Returns whether this instance is the active leader.
    /// </summary>
    bool IsLeader { get; }

    /// <summary>
    /// Returns "leader" or "follower" for log enrichment and telemetry tagging.
    /// </summary>
    string CurrentRole { get; }
}
