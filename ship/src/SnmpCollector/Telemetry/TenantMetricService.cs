using System.Diagnostics;
using System.Diagnostics.Metrics;
using SnmpCollector.Pipeline;

namespace SnmpCollector.Telemetry;

/// <summary>
/// Singleton service that owns all 8 tenant metric instruments on the SnmpCollector.Tenant meter.
/// Creating instruments here (once) avoids duplicate instrument registration and provides a single
/// injection point for all tenant evaluation code that needs to record metrics.
/// The SnmpCollector.Tenant meter is exported by ALL instances (no leader gate).
/// Instruments: 6 percentage gauges, 1 state gauge, 1 duration histogram.
/// </summary>
public sealed class TenantMetricService : ITenantMetricService, IDisposable
{
    private readonly Meter _meter;

    // Percentage of stale metric slots for the tenant (0.0-100.0)
    private readonly Gauge<double> _metricStalePercent;

    // Percentage of resolved (violated) metric slots for the tenant (0.0-100.0); higher = worse
    private readonly Gauge<double> _metricResolvedPercent;

    // Percentage of evaluate metric slots for the tenant (0.0-100.0)
    private readonly Gauge<double> _metricEvaluatePercent;

    // Percentage of SET commands dispatched for the tenant (0.0-100.0)
    private readonly Gauge<double> _commandDispatchedPercent;

    // Percentage of SET commands that failed for the tenant (0.0-100.0)
    private readonly Gauge<double> _commandFailedPercent;

    // Percentage of SET commands suppressed for the tenant (0.0-100.0)
    private readonly Gauge<double> _commandSuppressedPercent;

    // Gauge recording the tenant evaluation state (0=NotReady, 1=Healthy, 2=Resolved, 3=Unresolved)
    private readonly Gauge<double> _tenantEvaluationState;

    // Histogram of per-tenant evaluation cycle durations in milliseconds
    private readonly Histogram<double> _evaluationDuration;


    public TenantMetricService(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(TelemetryConstants.TenantMeterName);

        _metricStalePercent        = _meter.CreateGauge<double>("tenant.metric.stale.percent");
        _metricResolvedPercent     = _meter.CreateGauge<double>("tenant.metric.resolved.percent");
        _metricEvaluatePercent     = _meter.CreateGauge<double>("tenant.metric.evaluate.percent");
        _commandDispatchedPercent  = _meter.CreateGauge<double>("tenant.command.dispatched.percent");
        _commandFailedPercent      = _meter.CreateGauge<double>("tenant.command.failed.percent");
        _commandSuppressedPercent  = _meter.CreateGauge<double>("tenant.command.suppressed.percent");

        _tenantEvaluationState = _meter.CreateGauge<double>("tenant.evaluation.state");

        _evaluationDuration = _meter.CreateHistogram<double>(
            "tenant.evaluation.duration.milliseconds",
            description: "Duration of one tenant evaluation cycle in milliseconds");

    }

    /// <summary>Record the stale metric percentage for the given tenant (0.0-100.0).</summary>
    public void RecordMetricStalePercent(string tenantId, int priority, double percent)
        => _metricStalePercent.Record(percent, new TagList { { "tenant_id", tenantId }, { "priority", priority } });

    /// <summary>Record the resolved metric percentage for the given tenant (0.0-100.0).</summary>
    public void RecordMetricResolvedPercent(string tenantId, int priority, double percent)
        => _metricResolvedPercent.Record(percent, new TagList { { "tenant_id", tenantId }, { "priority", priority } });

    /// <summary>Record the evaluate metric percentage for the given tenant (0.0-100.0).</summary>
    public void RecordMetricEvaluatePercent(string tenantId, int priority, double percent)
        => _metricEvaluatePercent.Record(percent, new TagList { { "tenant_id", tenantId }, { "priority", priority } });

    /// <summary>Record the dispatched command percentage for the given tenant (0.0-100.0).</summary>
    public void RecordCommandDispatchedPercent(string tenantId, int priority, double percent)
        => _commandDispatchedPercent.Record(percent, new TagList { { "tenant_id", tenantId }, { "priority", priority } });

    /// <summary>Record the failed command percentage for the given tenant (0.0-100.0).</summary>
    public void RecordCommandFailedPercent(string tenantId, int priority, double percent)
        => _commandFailedPercent.Record(percent, new TagList { { "tenant_id", tenantId }, { "priority", priority } });

    /// <summary>Record the suppressed command percentage for the given tenant (0.0-100.0).</summary>
    public void RecordCommandSuppressedPercent(string tenantId, int priority, double percent)
        => _commandSuppressedPercent.Record(percent, new TagList { { "tenant_id", tenantId }, { "priority", priority } });

    /// <summary>Record the current tenant evaluation state as a gauge integer.</summary>
    public void RecordTenantState(string tenantId, int priority, TenantState state)
        => _tenantEvaluationState.Record((double)(int)state, new TagList { { "tenant_id", tenantId }, { "priority", priority } });

    /// <summary>Record the duration of one tenant evaluation cycle in milliseconds.</summary>
    public void RecordEvaluationDuration(string tenantId, int priority, double durationMs)
        => _evaluationDuration.Record(durationMs, new TagList { { "tenant_id", tenantId }, { "priority", priority } });

    public void Dispose() => _meter.Dispose();
}
