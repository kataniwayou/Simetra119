using System.Diagnostics;
using System.Diagnostics.Metrics;
using SnmpCollector.Pipeline;

namespace SnmpCollector.Telemetry;

/// <summary>
/// Singleton service that owns all 8 tenant metric instruments on the SnmpCollector.Tenant meter.
/// Creating instruments here (once) avoids duplicate instrument registration and provides a single
/// injection point for all tenant evaluation code that needs to record metrics.
/// The SnmpCollector.Tenant meter is exported by ALL instances (no leader gate).
/// </summary>
public sealed class TenantMetricService : ITenantMetricService, IDisposable
{
    private readonly Meter _meter;

    // Tier-1: counts stale metric slots per tenant per cycle
    private readonly Counter<long> _tier1Stale;

    // Tier-2: counts resolved metric slots per tenant per cycle
    private readonly Counter<long> _tier2Resolved;

    // Tier-3: counts evaluate metric slots per tenant per cycle
    private readonly Counter<long> _tier3Evaluate;

    // Counts SET commands dispatched for the tenant
    private readonly Counter<long> _commandDispatched;

    // Counts SET commands that failed for the tenant
    private readonly Counter<long> _commandFailed;

    // Counts SET commands suppressed for the tenant
    private readonly Counter<long> _commandSuppressed;

    // Gauge recording the tenant evaluation state (0=NotReady, 1=Healthy, 2=Resolved, 3=Unresolved)
    private readonly Gauge<double> _tenantState;

    // Histogram of per-tenant evaluation cycle durations in milliseconds
    private readonly Histogram<double> _evaluationDuration;

    public TenantMetricService(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(TelemetryConstants.TenantMeterName);

        _tier1Stale        = _meter.CreateCounter<long>("tenant.tier1.stale");
        _tier2Resolved     = _meter.CreateCounter<long>("tenant.tier2.resolved");
        _tier3Evaluate     = _meter.CreateCounter<long>("tenant.tier3.evaluate");
        _commandDispatched = _meter.CreateCounter<long>("tenant.command.dispatched");
        _commandFailed     = _meter.CreateCounter<long>("tenant.command.failed");
        _commandSuppressed = _meter.CreateCounter<long>("tenant.command.suppressed");

        _tenantState = _meter.CreateGauge<double>("tenant.state");

        _evaluationDuration = _meter.CreateHistogram<double>(
            "tenant.evaluation.duration.milliseconds",
            description: "Duration of one tenant evaluation cycle in milliseconds");
    }

    /// <summary>Increment the tier-1 stale counter for the given tenant by 1.</summary>
    public void IncrementTier1Stale(string tenantId, int priority)
        => _tier1Stale.Add(1, new TagList { { "tenant_id", tenantId }, { "priority", priority } });

    /// <summary>Increment the tier-2 resolved counter for the given tenant by 1.</summary>
    public void IncrementTier2Resolved(string tenantId, int priority)
        => _tier2Resolved.Add(1, new TagList { { "tenant_id", tenantId }, { "priority", priority } });

    /// <summary>Increment the tier-3 evaluate counter for the given tenant by 1.</summary>
    public void IncrementTier3Evaluate(string tenantId, int priority)
        => _tier3Evaluate.Add(1, new TagList { { "tenant_id", tenantId }, { "priority", priority } });

    /// <summary>Increment the command dispatched counter for the given tenant by 1.</summary>
    public void IncrementCommandDispatched(string tenantId, int priority)
        => _commandDispatched.Add(1, new TagList { { "tenant_id", tenantId }, { "priority", priority } });

    /// <summary>Increment the command failed counter for the given tenant by 1.</summary>
    public void IncrementCommandFailed(string tenantId, int priority)
        => _commandFailed.Add(1, new TagList { { "tenant_id", tenantId }, { "priority", priority } });

    /// <summary>Increment the command suppressed counter for the given tenant by 1.</summary>
    public void IncrementCommandSuppressed(string tenantId, int priority)
        => _commandSuppressed.Add(1, new TagList { { "tenant_id", tenantId }, { "priority", priority } });

    /// <summary>Record the current tenant evaluation state as a gauge integer.</summary>
    public void RecordTenantState(string tenantId, int priority, TenantState state)
        => _tenantState.Record((double)(int)state, new TagList { { "tenant_id", tenantId }, { "priority", priority } });

    /// <summary>Record the duration of one tenant evaluation cycle in milliseconds.</summary>
    public void RecordEvaluationDuration(string tenantId, int priority, double durationMs)
        => _evaluationDuration.Record(durationMs, new TagList { { "tenant_id", tenantId }, { "priority", priority } });

    public void Dispose() => _meter.Dispose();
}
