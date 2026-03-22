namespace SnmpCollector.Pipeline;

/// <summary>
/// Represents the evaluation state of a tenant after one SnapshotJob cycle.
/// Replaces the former internal SnapshotJob.TierResult enum.
/// Values are recorded as gauge integers via TenantMetricService (tenant.state instrument):
/// 0 = NotReady, 1 = Healthy, 2 = Resolved, 3 = Unresolved.
/// </summary>
public enum TenantState
{
    /// <summary>Tenant is not yet ready for evaluation (e.g., data not seeded).</summary>
    NotReady = 0,

    /// <summary>Tenant is healthy — no evaluation required this cycle.</summary>
    Healthy = 1,

    /// <summary>Tenant was evaluated and successfully resolved.</summary>
    Resolved = 2,

    /// <summary>Tenant was evaluated but could not be resolved.</summary>
    Unresolved = 3
}
