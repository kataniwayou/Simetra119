namespace SnmpCollector.Telemetry;

/// <summary>
/// Default implementation for local development / non-K8s environments. Always reports leader status.
/// Replaced by K8sLeaseElection when running inside Kubernetes.
/// </summary>
public sealed class AlwaysLeaderElection : ILeaderElection
{
    /// <inheritdoc />
    public bool IsLeader => true;

    /// <inheritdoc />
    public string CurrentRole => "leader";
}
