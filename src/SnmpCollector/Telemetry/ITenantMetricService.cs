using SnmpCollector.Pipeline;

namespace SnmpCollector.Telemetry;

/// <summary>
/// Service for recording per-tenant OTel metrics on the SnmpCollector.Tenant meter.
/// Exposes 8 instruments: 6 percentage gauges, 1 state gauge, 1 duration histogram.
/// All methods accept tenant_id and priority tags.
/// The meter is exported by ALL instances (not leader-gated).
/// </summary>
public interface ITenantMetricService
{
    /// <summary>Record the stale metric percentage for the given tenant (0.0-100.0).</summary>
    void RecordMetricStalePercent(string tenantId, int priority, double percent);

    /// <summary>Record the resolved metric percentage for the given tenant (0.0-100.0). Higher = more violated holders.</summary>
    void RecordMetricResolvedPercent(string tenantId, int priority, double percent);

    /// <summary>Record the evaluate metric percentage for the given tenant (0.0-100.0).</summary>
    void RecordMetricEvaluatePercent(string tenantId, int priority, double percent);

    /// <summary>Record the dispatched command percentage for the given tenant (0.0-100.0).</summary>
    void RecordCommandDispatchedPercent(string tenantId, int priority, double percent);

    /// <summary>Record the failed command percentage for the given tenant (0.0-100.0).</summary>
    void RecordCommandFailedPercent(string tenantId, int priority, double percent);

    /// <summary>Record the suppressed command percentage for the given tenant (0.0-100.0).</summary>
    void RecordCommandSuppressedPercent(string tenantId, int priority, double percent);

    /// <summary>Record the current tenant evaluation state as a gauge integer.</summary>
    void RecordTenantState(string tenantId, int priority, TenantState state);

    /// <summary>Record the duration of one tenant evaluation cycle in milliseconds.</summary>
    void RecordEvaluationDuration(string tenantId, int priority, double durationMs);
}
