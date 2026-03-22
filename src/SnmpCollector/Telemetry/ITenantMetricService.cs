using SnmpCollector.Pipeline;

namespace SnmpCollector.Telemetry;

/// <summary>
/// Service for recording per-tenant OTel metrics on the SnmpCollector.Tenant meter.
/// All methods accept tenant_id and priority tags.
/// The meter is exported by ALL instances (not leader-gated).
/// </summary>
public interface ITenantMetricService
{
    /// <summary>Increment the tier-1 stale counter for the given tenant by 1.</summary>
    void IncrementTier1Stale(string tenantId, int priority);

    /// <summary>Increment the tier-2 resolved counter for the given tenant by 1.</summary>
    void IncrementTier2Resolved(string tenantId, int priority);

    /// <summary>Increment the tier-3 evaluate counter for the given tenant by 1.</summary>
    void IncrementTier3Evaluate(string tenantId, int priority);

    /// <summary>Increment the command dispatched counter for the given tenant by 1.</summary>
    void IncrementCommandDispatched(string tenantId, int priority);

    /// <summary>Increment the command failed counter for the given tenant by 1.</summary>
    void IncrementCommandFailed(string tenantId, int priority);

    /// <summary>Increment the command suppressed counter for the given tenant by 1.</summary>
    void IncrementCommandSuppressed(string tenantId, int priority);

    /// <summary>Record the current tenant evaluation state as a gauge integer.</summary>
    void RecordTenantState(string tenantId, int priority, TenantState state);

    /// <summary>Record the duration of one tenant evaluation cycle in milliseconds.</summary>
    void RecordEvaluationDuration(string tenantId, int priority, double durationMs);
}
